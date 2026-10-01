using System.Net;
using System.Net.Http.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Api;
using MarketSignalAI.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace MarketSignalAI.Tests;
public sealed class AnalysisScheduleSettingsTests
{
 [Fact] public async Task ApiPersistsAddEditDeleteAndEnabledWithoutExecutingAnalysis()
 {
  await using var factory=new ApiFactory();using var client=factory.CreateClient();
  var original=await client.GetFromJsonAsync<AnalysisScheduleView>("/api/settings/analysis-schedule");
  Assert.Equal("Asia/Taipei",original!.Timezone);Assert.Equal("08:30",original.TradingDayCheck);Assert.Equal(4,original.Times.Count);
  Assert.Equal(HttpStatusCode.Forbidden,(await client.PutAsJsonAsync("/api/settings/analysis-schedule",new{enabled=false,times=new[]{"11:15"}})).StatusCode);
  client.DefaultRequestHeaders.Add("X-Market-Signal","web");
  foreach(var times in new[]{new[]{"11:15","13:20"},new[]{"11:30"},Array.Empty<string>()})
  {
   Assert.Equal(HttpStatusCode.NoContent,(await client.PutAsJsonAsync("/api/settings/analysis-schedule",new{enabled=false,times})).StatusCode);
   var saved=await client.GetFromJsonAsync<AnalysisScheduleView>("/api/settings/analysis-schedule");Assert.False(saved!.Enabled);Assert.Equal(times,saved.Times);
  }
  var store=factory.Services.GetRequiredService<IAnalysisScheduleStore>();Assert.True((await store.GetAsync(2,default)).Enabled);
 }
 [Theory][InlineData("25:00")][InlineData("9:05")][InlineData("09:05,09:05")][InlineData("")]
 public async Task InvalidTimesDoNotMutate(string times)
 {
  await using var factory=new ApiFactory();using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Market-Signal","web");
  Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync("/api/settings/analysis-schedule",new{enabled=true,times=times.Split(',')})).StatusCode);
  Assert.Equal(4,(await client.GetFromJsonAsync<AnalysisScheduleView>("/api/settings/analysis-schedule"))!.Times.Count);
 }
 [Fact] public async Task WorkerReadsEditsAndDisabledScheduleDoesNotRemoveOpeningCheck()
 {
  var settings=new MemoryAnalysisScheduleStore();var executor=new Recorder();var clock=new Clock();
  using var services=new ServiceCollection().AddSingleton<IAnalysisScheduleStore>(settings).AddSingleton<IMarketScheduleExecutor>(executor).BuildServiceProvider();
  using var worker=new MarketAnalysisWorker(services.GetRequiredService<IServiceScopeFactory>(),NullLogger<MarketAnalysisWorker>.Instance,clock);
  await settings.SaveAsync(1,new(false,[]),default);clock.Now=new(2026,10,1,0,30,0,TimeSpan.Zero);await worker.RunDueAsync(default);Assert.Equal(new[]{true},executor.Calls);
  clock.Now=new(2026,10,1,1,5,0,TimeSpan.Zero);await worker.RunDueAsync(default);Assert.Single(executor.Calls);
  await settings.SaveAsync(1,new(true,["09:05"]),default);await worker.RunDueAsync(default);await worker.RunDueAsync(default);Assert.Equal(new[]{true,false},executor.Calls);
  using var restarted=new MarketAnalysisWorker(services.GetRequiredService<IServiceScopeFactory>(),NullLogger<MarketAnalysisWorker>.Instance,clock);await restarted.RunDueAsync(default);Assert.Equal(2,executor.Calls.Count);
  await settings.SaveAsync(1,new(true,["10:20"]),default);clock.Now=new(2026,10,1,2,20,0,TimeSpan.Zero);await restarted.RunDueAsync(default);Assert.Equal(3,executor.Calls.Count);
  clock.Now=new(2026,10,2,2,20,0,TimeSpan.Zero);await restarted.RunDueAsync(default);Assert.Equal(4,executor.Calls.Count);
 }
 private sealed class Clock:TimeProvider {public DateTimeOffset Now;public override DateTimeOffset GetUtcNow()=>Now;}
 private sealed class Recorder:IMarketScheduleExecutor {public List<bool> Calls=[];public Task ExecuteAsync(DateOnly date,bool isOpeningCheck,CancellationToken ct){Calls.Add(isOpeningCheck);return Task.CompletedTask;}}
}
