using System.Text.Json;
using MarketSignalAI.Application;

namespace MarketSignalAI.Infrastructure;

// Explicit placeholder: no external AI service is called.
public sealed class MockAIAnalyst(string providerCode) : IAIAnalyst
{
    public string ProviderCode { get; } = providerCode;
    public Task<AnalystResult> AnalyzeAsync(MarketContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var analysis = DemoData.MockResult(DemoData.MockAction(context.Product, ProviderCode), context.Product.IsLeveraged);
        return Task.FromResult(new AnalystResult("mock-v1", analysis, JsonSerializer.Serialize(new { isMock = true, analysis })));
    }
}
