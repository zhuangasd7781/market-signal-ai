using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
namespace MarketSignalAI.Infrastructure;

// Single audit serialization point: property order, casing and values match the legacy Runner.
internal static class AnalysisInputSnapshot
{
    internal static string Serialize(PreparedAnalysisContext prepared, string? configuredModel, AnalystResult answer)
    {
        var context = prepared.Context;
        var promptSnapshot = context.Prompt!;
        var product = context.Product;
        var snapshot = context.Snapshot;
        var history = context.History;
        var position = context.Position;
        var sharedInput = prepared.SharedInput;
        var twContext = context.TwMarketContext;
        var targetReturns = context.TargetReturns;
        var newsContext = context.NewsContext;
        return JsonSerializer.Serialize(new { promptVersion = promptSnapshot.Version, promptVersionId = promptSnapshot.Id, promptSnapshot, skillIdentifiers = promptSnapshot.Skills.Select(x => x.Identifier).ToArray(), configuredModel, isMock = answer.Model.StartsWith("mock", StringComparison.OrdinalIgnoreCase), product, snapshot, history, position, previousDecision = context.PreviousDecision, marketReferences = context.MarketReferences, previousDecisions = context.PreviousDecisions, analysisInput = sharedInput, twMarketContext = twContext, targetReturns, newsContext, instructions = answer.Instructions, reasoningEffort = answer.ReasoningEffort });
    }
}
