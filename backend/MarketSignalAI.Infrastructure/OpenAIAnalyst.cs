using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Infrastructure;

public sealed class OpenAIAnalyst(HttpClient http, OpenAIAnalystOptions options) : IAIAnalyst
{
    public string ProviderCode => "openai";
    public string Model => options.Model;
    public TimeSpan Timeout => TimeSpan.FromSeconds(options.TimeoutSeconds);
    public string ReasoningEffort => options.ReasoningEffort;
    public async Task<AnalystResult> AnalyzeAsync(MarketContext context, CancellationToken ct)
    {
        options.Validate();
        var instructions = AnalysisProtocol.Instructions(context);
        var input = AnalysisProtocol.Input(context);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = options.Model, store = false, instructions, input,
            max_output_tokens = options.MaxOutputTokens,
            reasoning = new { effort = options.ReasoningEffort },
            text = new { format = new { type = "json_schema", name = "market_analysis", strict = true, schema = AnalysisProtocol.Schema } }
        });
        using var response = await http.SendAsync(request, ct);
        // Do not include upstream response bodies in exceptions/logs.
        if (!response.IsSuccessStatusCode)
            throw new AIProviderException($"OpenAI returned HTTP {(int)response.StatusCode}.");
        var raw = await response.Content.ReadAsStringAsync(ct);
        TokenUsage? usage = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            {
                var inputTokens = u.GetProperty("input_tokens").GetInt64();
                var outputTokens = u.GetProperty("output_tokens").GetInt64();
                if (inputTokens < 0 || outputTokens < 0) throw new InvalidDataException("OpenAI usage is invalid.");
                long? cached = u.TryGetProperty("input_tokens_details", out var details) && details.TryGetProperty("cached_tokens", out var c) ? c.GetInt64() : null;
                usage = new(inputTokens, outputTokens, cached);
            }
            if (root.GetProperty("status").GetString() != "completed")
                throw new InvalidDataException("OpenAI response did not complete.");
            var model = root.GetProperty("model").GetString();
            var texts = new List<string>();
            foreach (var item in root.GetProperty("output").EnumerateArray())
            {
                if (item.GetProperty("type").GetString() != "message") continue;
                if (item.GetProperty("status").GetString() != "completed")
                    throw new InvalidDataException("OpenAI message did not complete.");
                foreach (var content in item.GetProperty("content").EnumerateArray())
                {
                    var type = content.GetProperty("type").GetString();
                    if (type == "refusal") throw new InvalidDataException("OpenAI declined this analysis.");
                    if (type == "output_text") texts.Add(content.GetProperty("text").GetString() ?? "");
                }
            }
            if (texts.Count != 1 || string.IsNullOrWhiteSpace(model) || model.Length > 100 || model.StartsWith("mock", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("OpenAI returned no unambiguous analysis or model.");
            using var resultJson = JsonDocument.Parse(texts[0]);
            AnalysisProtocol.RequireFields(resultJson.RootElement, AnalysisProtocol.Schema);
            var analysis = resultJson.RootElement.Deserialize<Analysis>(AnalysisProtocol.JsonOptions)
                ?? throw new InvalidDataException("OpenAI returned no analysis.");
            if (usage is null) throw new InvalidDataException("OpenAI response is missing token usage.");
            var result = new AnalystResult(model, analysis, raw, instructions, usage, options.ReasoningEffort);
            AnalysisResultValidator.Validate(result);
            AnalysisProtocol.ValidatePosition(analysis, context.Position);
            return result;
        }
        catch (InvalidDataException ex) { throw new AIProviderException(ex.Message, usage); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new AIProviderException("OpenAI returned an invalid analysis response.", usage); }
    }

}
