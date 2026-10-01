using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Infrastructure;

public sealed class DeepSeekAnalyst(HttpClient http, DeepSeekAnalystOptions options) : IAIAnalyst
{
    public string ProviderCode => "deepseek";
    public string Model => options.Model;
    public TimeSpan Timeout => TimeSpan.FromSeconds(options.TimeoutSeconds);
    public IAIAnalyst WithModel(string model) => new DeepSeekAnalyst(http,new DeepSeekAnalystOptions { Enabled=options.Enabled,ApiKey=options.ApiKey,Model=model,BaseUrl=options.BaseUrl,TimeoutSeconds=options.TimeoutSeconds,MaxOutputTokens=options.MaxOutputTokens });
    public async Task<AnalystResult> AnalyzeAsync(MarketContext context, CancellationToken ct)
    {
        options.Validate();
        var instructions = AnalysisProtocol.Instructions(context) + "\nReturn only one JSON object. For HOLD/EXIT, quantity MUST be the JSON literal null, NEVER 0 or a string, including EVERY nextActions entry. Confidence MUST be an integer from 0 to 100. All explanatory lists MUST have at least one nonempty string. Conform exactly to this JSON schema: " + AnalysisProtocol.Schema.GetRawText();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/"), "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = options.Model, stream = false, max_tokens = options.MaxOutputTokens,
            response_format = new { type = "json_object" },
            messages = new[] { new { role = "system", content = instructions }, new { role = "user", content = AnalysisProtocol.Input(context) } }
        });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new AIProviderException($"DeepSeek returned HTTP {(int)response.StatusCode}.");
        var raw = await response.Content.ReadAsStringAsync(ct);
        TokenUsage? usage = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            {
                long? cached = u.TryGetProperty("prompt_cache_hit_tokens", out var c) ? c.GetInt64() :
                    u.TryGetProperty("prompt_tokens_details", out var d) && d.TryGetProperty("cached_tokens", out c) ? c.GetInt64() : null;
                usage = new(u.GetProperty("prompt_tokens").GetInt64(), u.GetProperty("completion_tokens").GetInt64(), cached);
            }
            var choices = root.GetProperty("choices");
            if (choices.GetArrayLength() != 1 || choices[0].GetProperty("finish_reason").GetString() != "stop")
                throw new InvalidDataException("DeepSeek response did not complete unambiguously.");
            var model = root.GetProperty("model").GetString();
            if (string.IsNullOrWhiteSpace(model) || model.StartsWith("mock", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DeepSeek response is missing a real model.");
            using var resultJson = JsonDocument.Parse(choices[0].GetProperty("message").GetProperty("content").GetString() ?? "");
            AnalysisProtocol.RequireFields(resultJson.RootElement, AnalysisProtocol.Schema);
            var analysis = resultJson.RootElement.Deserialize<Analysis>(AnalysisProtocol.JsonOptions)
                ?? throw new InvalidDataException("DeepSeek returned no analysis.");
            if (usage is null) throw new InvalidDataException("DeepSeek response is missing token usage.");
            var result = new AnalystResult(model, analysis, raw, instructions, usage);
            AnalysisResultValidator.Validate(result);
            AnalysisProtocol.ValidatePosition(analysis, context.Position);
            return result;
        }
        catch (InvalidDataException ex) { throw new AIProviderException(ex.Message, usage); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new AIProviderException("DeepSeek returned an invalid analysis response.", usage); }
    }
}
