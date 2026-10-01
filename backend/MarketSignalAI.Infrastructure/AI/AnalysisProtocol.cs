using System.Text.Json;
using System.Text.Json.Serialization;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Infrastructure;

// Both live providers use the existing schema, skills, sanitized input and validation.
internal static class AnalysisProtocol
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    internal static readonly JsonElement Schema = JsonSerializer.Deserialize<JsonElement>(ReadResource("AI.analysis.schema.json"));
    internal static readonly string CommonInstructions = ReadResource("AI.Skills.market-evidence.md") + "\n" + ReadResource("AI.Skills.decision-rules.md");
    internal static readonly string LeveragedInstructions = ReadResource("AI.Skills.leveraged-etf.md");

    internal static string Instructions(MarketContext context) => CommonInstructions + (context.Product.IsLeveraged ? "\n" + LeveragedInstructions : "") +
        (context.Position?.Quantity is not > 0
            ? "\nThe supplied context has NO positive position. For action AND EVERY nextActions entry, REDUCE and EXIT are forbidden, even under hypothetical future conditions. Do not assume future ownership. Without a supplied purchase budget, use HOLD with quantity null and state what evidence is missing."
            : $"\nAvailable position is {context.Position.Quantity} {context.Product.QuantityUnit}. Every REDUCE quantity must be no greater than this amount; do not assume future purchases.");
    internal static string Input(MarketContext context) => JsonSerializer.Serialize(new
    {
        product = context.Product, snapshot = context.Snapshot, history = context.History,
        position = context.Position is { } p ? new { p.Quantity, p.AverageCost, p.UpdatedAt } : null,
        previousDecision = context.PreviousDecision, previousDecisions = context.PreviousDecisions, marketReferences = context.MarketReferences
    }, JsonOptions);
    // Require explicit fields even when the CLR default (e.g. confidence=0) would deserialize.
    internal static void RequireFields(JsonElement value, JsonElement schema)
    {
        if (schema.GetProperty("type").ValueKind != JsonValueKind.String) return;
        if (schema.GetProperty("type").GetString() == "object")
        {
            foreach (var required in schema.GetProperty("required").EnumerateArray())
                if (!value.TryGetProperty(required.GetString()!, out _)) throw new InvalidDataException("AI analysis is missing a required field.");
            foreach (var property in schema.GetProperty("properties").EnumerateObject())
                RequireFields(value.GetProperty(property.Name), property.Value);
        }
        else if (schema.GetProperty("type").GetString() == "array")
            foreach (var item in value.EnumerateArray()) RequireFields(item, schema.GetProperty("items"));
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(x => x.GetString() == value.GetString()))
            throw new InvalidDataException("AI analysis contains an invalid enum value.");
    }

    internal static void ValidatePosition(Analysis analysis, UserPosition? position)
    {
        void Check(string action, decimal? quantity)
        {
            if ((action is "REDUCE" or "EXIT") && position?.Quantity is not > 0 ||
                action == "REDUCE" && quantity > position?.Quantity)
                throw new InvalidDataException("AI analysis exceeds the available position.");
        }
        Check(analysis.Action, analysis.Quantity);
        foreach (var next in analysis.NextActions) Check(next.Action, next.Quantity);
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(OpenAIAnalyst).Assembly.GetManifestResourceStream("MarketSignalAI.Infrastructure." + name)
            ?? throw new InvalidOperationException($"Missing AI resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
