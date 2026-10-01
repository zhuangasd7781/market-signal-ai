using System.Text.Json;
using MarketSignalAI.Domain;
namespace MarketSignalAI.Application;

public sealed record PositionContextView(decimal Quantity, decimal AverageCost);
public sealed record AnalysisContextView(decimal? TargetPrice, IReadOnlyList<MarketReferenceContext> MarketReferences,
    PositionContextView? Position, IReadOnlyList<string> SkillIdentifiers, string? ConfiguredModel, long? PromptVersionId);
internal static class AnalysisContextProjection
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    internal static (string? Version, AnalysisContextView? Context) Read(string input)
    {
        try
        {
            using var doc = JsonDocument.Parse(input);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            JsonElement? Find(JsonElement value, string name) => value.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value is { ValueKind: not JsonValueKind.Undefined } found ? found : null;
            var version = Find(root, "promptVersion")?.GetString();
            var snapshot = Find(root, "snapshot");
            var price = snapshot is { ValueKind: JsonValueKind.Object } snap ? Find(snap, "price")?.GetDecimal() : null;
            var refs = Find(root, "marketReferences")?.Deserialize<MarketReferenceContext[]>(Options) ?? [];
            var position = Find(root, "position")?.Deserialize<PositionContextView>(Options);
            var skills = Find(root, "skillIdentifiers")?.Deserialize<string[]>(Options) ?? [];
            var configured = Find(root, "configuredModel")?.GetString();
            var id = Find(root, "promptVersionId")?.GetInt64();
            if (price is null && refs.Length == 0 && position is null && version is null) return (null, null);
            return (version, new(price, refs, position, skills, configured, id));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or OverflowException) { return (null, null); }
    }
}
