using FastEndpoints;
using MarketSignalAI.Application;
namespace MarketSignalAI.Api;
public sealed record AnalysisScheduleView(bool Enabled,IReadOnlyList<string> Times,string Timezone="Asia/Taipei",string TradingDayCheck="08:30");
public sealed class GetAnalysisScheduleEndpoint(IAnalysisScheduleStore store,ICurrentUser user) : EndpointWithoutRequest<AnalysisScheduleView>
{
    public override void Configure(){ Get("/api/settings/analysis-schedule"); AllowAnonymous(); }
    public override async Task HandleAsync(CancellationToken ct) { var row=await store.GetAsync(user.UserId,ct); await SendAsync(new(row.Enabled,row.Times),cancellation:ct); }
}
public sealed class SaveAnalysisScheduleRequest { public bool? Enabled {get;set;} public string[]? Times {get;set;} }
public sealed class SaveAnalysisScheduleEndpoint(IAnalysisScheduleStore store,ICurrentUser user) : Endpoint<SaveAnalysisScheduleRequest>
{
    public override void Configure(){ Put("/api/settings/analysis-schedule"); AllowAnonymous(); }
    public override async Task HandleAsync(SaveAnalysisScheduleRequest req,CancellationToken ct)
    {
        if(req.Enabled is null || req.Times is null) throw new ArgumentException("Enabled and Times are required.");
        await store.SaveAsync(user.UserId,new(req.Enabled.Value,req.Times),ct); await SendNoContentAsync(ct);
    }
}
