using System.Net;
using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Infrastructure;
using Xunit;
namespace MarketSignalAI.Tests;
public sealed class DeepSeekNewsCollectorTests
{
    private static string Output()=>JsonSerializer.Serialize(new {events=new[]{NewsIntelligenceTests.Event()}},new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static string Response(string? content=null,string finish="stop",bool usage=true)=>JsonSerializer.Serialize(new {model="deepseek-v4-flash",usage=usage ? new {prompt_tokens=123,completion_tokens=45,prompt_cache_hit_tokens=20} : null,choices=new[]{new {finish_reason=finish,message=new {content=content ?? Output()}}}});
    private static NewsIntelligenceInput Input(){var a=NewsIntelligenceTests.Article();return new(a.PublishedAt!.Value.AddHours(-72),a.PublishedAt.Value.AddMinutes(30),[a]);}
    [Fact]
    public async Task RealClientUsesDedicatedFlashPromptSuppliedSourcesAndActualUsage()
    {
        using var http=new HttpClient(new OpenAIAnalystTests.Handler(async(req,ct)=>{
            Assert.Equal("Bearer",req.Headers.Authorization!.Scheme);Assert.Equal("test-only",req.Headers.Authorization.Parameter);
            using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));var root=body.RootElement;
            Assert.Equal("deepseek-flash",root.GetProperty("model").GetString());Assert.Equal("disabled",root.GetProperty("thinking").GetProperty("type").GetString());
            Assert.Contains("NOT an investment decision maker",root.GetProperty("messages")[0].GetProperty("content").GetString()!);
            Assert.Contains("example.com/one",root.GetProperty("messages")[1].GetProperty("content").GetString()!);
            return OpenAIAnalystTests.Json(Response());
        }));
        var result=await new DeepSeekNewsCollector(http,new(){ApiKey="test-only"},new()).CollectAsync(Input(),default);
        Assert.Equal("deepseek-v4-flash",result.ActualModel);Assert.Equal(123,result.Usage.InputTokens);Assert.Equal(45,result.Usage.OutputTokens);Assert.Equal(20,result.Usage.CachedTokens);Assert.Single(result.Events);
    }
    [Theory]
    [InlineData("{\"events\":[],\"action\":\"HOLD\"}")]
    [InlineData("{\"events\":[{\"title\":\"missing fields\"}]}")]
    [InlineData("not JSON")]
    public async Task MalformedMissingFieldsAndUnexpectedActionFailWithActualUsage(string content)
    {
        using var http=new HttpClient(new OpenAIAnalystTests.Handler((req,ct)=>Task.FromResult(OpenAIAnalystTests.Json(Response(content)))));
        var error=await Assert.ThrowsAsync<NewsPipelineException>(()=>new DeepSeekNewsCollector(http,new(){ApiKey="test-only"},new()).CollectAsync(Input(),default));Assert.Equal(123,error.Usage!.InputTokens);
    }
    [Theory]
    [InlineData("length",true)]
    [InlineData("stop",false)]
    public async Task TruncationOrMissingUsageNeverReturnsSuccess(string finish,bool usage)
    {
        using var http=new HttpClient(new OpenAIAnalystTests.Handler((req,ct)=>Task.FromResult(OpenAIAnalystTests.Json(Response(finish:finish,usage:usage)))));
        await Assert.ThrowsAsync<NewsPipelineException>(()=>new DeepSeekNewsCollector(http,new(){ApiKey="test-only"},new()).CollectAsync(Input(),default));
    }
    [Fact]
    public async Task HttpFailureReportsStatusWithoutEchoingResponseOrSecret()
    {
        using var http=new HttpClient(new OpenAIAnalystTests.Handler((req,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests){Content=new StringContent("sensitive upstream body")})));
        var error=await Assert.ThrowsAsync<NewsPipelineException>(()=>new DeepSeekNewsCollector(http,new(){ApiKey="test-only"},new()).CollectAsync(Input(),default));Assert.Contains("429",error.Message);Assert.DoesNotContain("sensitive",error.Message);Assert.DoesNotContain("test-only",error.Message);
    }
}
