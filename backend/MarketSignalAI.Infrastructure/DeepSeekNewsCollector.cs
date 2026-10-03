using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
namespace MarketSignalAI.Infrastructure;

public sealed class DeepSeekNewsCollector(HttpClient http,DeepSeekAnalystOptions deepSeek,NewsOptions options) : INewsIntelligenceCollector
{
    public string ConfiguredModel=>options.Model;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) { UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow };
    private static readonly string Prompt=Resource("AI.news-intelligence.md");
    private static readonly string Schema=Resource("AI.news.schema.json");
    private sealed record Output(NewsEventDraft[] Events);
    public async Task<NewsCollection> CollectAsync(NewsIntelligenceInput input,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(deepSeek.ApiKey)) throw new NewsPipelineException("FLASH","DeepSeek API 尚未設定，未建立市場情報。",ConfiguredModel);
        if(string.IsNullOrWhiteSpace(options.Model) || !options.Model.Contains("flash",StringComparison.OrdinalIgnoreCase) || options.Model.Length>100)
            throw new NewsPipelineException("FLASH","News:Model 必須設定為 Flash Model ID。",ConfiguredModel);
        var validation=new DeepSeekAnalystOptions {ApiKey=deepSeek.ApiKey,Model=options.Model,BaseUrl=deepSeek.BaseUrl,MaxOutputTokens=options.MaxOutputTokens,TimeoutSeconds=options.TimeoutSeconds};validation.Validate();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(new Uri(deepSeek.BaseUrl.TrimEnd('/')+"/"),"chat/completions"));
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",deepSeek.ApiKey);
        request.Content=JsonContent.Create(new {model=ConfiguredModel,stream=false,max_tokens=options.MaxOutputTokens,thinking=new {type="disabled"},
            response_format=new {type="json_object"},messages=new[]{new {role="system",content=Prompt+"\nJSON Schema:\n"+Schema},new {role="user",content=JsonSerializer.Serialize(input,Json)}}});
        using var response=await http.SendAsync(request,timeout.Token);
        if(!response.IsSuccessStatusCode) throw new NewsPipelineException("FLASH",$"DeepSeek Flash 回應 HTTP {(int)response.StatusCode}，未建立市場情報。",ConfiguredModel);
        var raw=await response.Content.ReadAsStringAsync(timeout.Token);TokenUsage? usage=null;string? model=null;
        try
        {
            using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;model=root.GetProperty("model").GetString();
            var u=root.GetProperty("usage");long? cached=u.TryGetProperty("prompt_cache_hit_tokens",out var c) ? c.GetInt64() : u.TryGetProperty("prompt_tokens_details",out var d) && d.TryGetProperty("cached_tokens",out c) ? c.GetInt64() : null;
            usage=new(u.GetProperty("prompt_tokens").GetInt64(),u.GetProperty("completion_tokens").GetInt64(),cached);
            var choices=root.GetProperty("choices");if(choices.GetArrayLength()!=1 || choices[0].GetProperty("finish_reason").GetString()!="stop") throw new InvalidDataException("Incomplete output");
            var content=choices[0].GetProperty("message").GetProperty("content").GetString()!;
            using var output=JsonDocument.Parse(content);using var schema=JsonDocument.Parse(Schema);ValidateSchema(output.RootElement,schema.RootElement);
            var events=output.RootElement.Deserialize<Output>(Json)?.Events ?? throw new InvalidDataException("Missing events");
            NewsContextRefresher.Validate(events,input);
            if(string.IsNullOrWhiteSpace(model) || !model.Contains("flash",StringComparison.OrdinalIgnoreCase) || usage.InputTokens<0 || usage.OutputTokens<0 || usage.CachedTokens<0 || usage.CachedTokens>usage.InputTokens)throw new InvalidDataException("Invalid model/usage");
            return new(events,model,usage,"news-intelligence-v1",Prompt,Schema,raw);
        }
        catch(Exception ex) when(ex is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or NewsPipelineException)
        {throw new NewsPipelineException("VALIDATION",$"DeepSeek Flash 回傳無效的 NewsContext JSON（{(ex is InvalidDataException or NewsPipelineException ? ex.Message : ex.GetType().Name)}），未建立市場情報。",model ?? ConfiguredModel,usage,raw);}
    }
    private static void ValidateSchema(JsonElement value,JsonElement schema)
    {
        var type=schema.GetProperty("type");var expected=type.ValueKind==JsonValueKind.String ? type.GetString()! : "nullable";
        if(expected=="nullable") {if(value.ValueKind is JsonValueKind.Null or JsonValueKind.String)return;throw new InvalidDataException("Invalid nullable value");}
        if(expected=="object")
        {
            if(value.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Object required");
            var props=schema.GetProperty("properties");foreach(var required in schema.GetProperty("required").EnumerateArray())if(!value.TryGetProperty(required.GetString()!,out _))throw new InvalidDataException("Missing field");
            foreach(var field in value.EnumerateObject()){if(!props.TryGetProperty(field.Name,out var child))throw new InvalidDataException("Unexpected field");ValidateSchema(field.Value,child);}
        }
        else if(expected=="array")
        {
            if(value.ValueKind!=JsonValueKind.Array || schema.TryGetProperty("minItems",out var min) && value.GetArrayLength()<min.GetInt32() || schema.TryGetProperty("maxItems",out var max) && value.GetArrayLength()>max.GetInt32())throw new InvalidDataException("Invalid array");
            foreach(var item in value.EnumerateArray())ValidateSchema(item,schema.GetProperty("items"));
        }
        else if(expected=="integer") {if(!value.TryGetInt32(out var n) || n<schema.GetProperty("minimum").GetInt32() || n>schema.GetProperty("maximum").GetInt32())throw new InvalidDataException("Invalid score");}
        else if(expected=="string")
        {
            if(value.ValueKind!=JsonValueKind.String)throw new InvalidDataException("String required");
            var text=value.GetString()!;if(schema.TryGetProperty("minLength",out var min) && text.Length<min.GetInt32() || schema.TryGetProperty("maxLength",out var max) && text.Length>max.GetInt32())throw new InvalidDataException("Invalid text length");
            if(schema.TryGetProperty("enum",out var choices) && !choices.EnumerateArray().Any(x=>x.GetString()==text))throw new InvalidDataException("Invalid enum");
        }
    }
    private static string Resource(string name){using var stream=typeof(DeepSeekNewsCollector).Assembly.GetManifestResourceStream("MarketSignalAI.Infrastructure."+name) ?? throw new InvalidOperationException("Missing news resource");using var reader=new StreamReader(stream);return reader.ReadToEnd();}
}
