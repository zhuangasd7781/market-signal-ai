namespace MarketSignalAI.Application;

public static class AnalysisResultValidator
{
    public static void Validate(AnalystResult result)
    {
        static bool ValidDecision(string? action, decimal? quantity) =>
            action is "ADD" or "REDUCE" ? quantity is > 0 and <= 999999999999m && decimal.Round(quantity.Value, 6) == quantity :
                action is "HOLD" or "EXIT" && quantity is null;
        static bool HasText(string[]? values) => values is { Length: > 0 } && values.All(x => !string.IsNullOrWhiteSpace(x));
        var a = result.Analysis;
        if (a is not null && !ValidDecision(a.Action, a.Quantity))
            throw new InvalidDataException("AI action/quantity failed validation: ADD/REDUCE require a positive quantity; HOLD/EXIT require null.");
        if (a?.NextActions is { } next && next.Any(x => x is not null && !ValidDecision(x.Action, x.Quantity)))
            throw new InvalidDataException("AI nextActions action/quantity failed validation: ADD/REDUCE require a positive quantity; HOLD/EXIT require null.");

        if (string.IsNullOrWhiteSpace(result.Model) || result.Model.Length > 100 || string.IsNullOrWhiteSpace(result.RawResponse) ||
            a is null || !ValidDecision(a.Action, a.Quantity) || a.Confidence is < 0 or > 100 || a.RootEvent is null ||
            string.IsNullOrWhiteSpace(a.RootEvent.Summary) || string.IsNullOrWhiteSpace(a.RootEvent.Direction) ||
            string.IsNullOrWhiteSpace(a.RootEvent.Status) || string.IsNullOrWhiteSpace(a.MarketRegime) ||
            string.IsNullOrWhiteSpace(a.Trend) || string.IsNullOrWhiteSpace(a.Momentum) ||
            string.IsNullOrWhiteSpace(a.Volume) || string.IsNullOrWhiteSpace(a.RiskReward) || string.IsNullOrWhiteSpace(a.Invalidation) ||
            !HasText(a.Reasons) || !HasText(a.Risks) || !HasText(a.BullCase) || !HasText(a.BearCase) || a.NextActions is not { Length: > 0 } ||
            a.NextActions.Any(x => x is null || string.IsNullOrWhiteSpace(x.Condition) || !ValidDecision(x.Action, x.Quantity)) ||
            result.Usage is { InputTokens: < 0 } or { OutputTokens: < 0 } or { CachedTokens: < 0 } || result.Usage?.CachedTokens > result.Usage?.InputTokens)
            throw new InvalidDataException("AI response failed validation.");
    }
}
