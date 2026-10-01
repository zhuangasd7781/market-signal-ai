namespace MarketSignalAI.Infrastructure;

public sealed class OpenAIAnalystOptions
{
    public bool Enabled { get; set; }
    public int TimeoutSeconds { get; set; } = 180;
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string ReasoningEffort { get; set; } = "";
    public int MaxOutputTokens { get; set; } = 8192;

    public void Validate()
    {
        if (TimeoutSeconds is < 1 or > 600) throw new InvalidOperationException("OpenAI:TimeoutSeconds must be between 1 and 600.");
        if (string.IsNullOrWhiteSpace(ApiKey)) throw new InvalidOperationException("OpenAI:ApiKey is missing.");
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 100 || Model.StartsWith("mock", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OpenAI:Model must be a real OpenAI model ID of at most 100 characters.");
        if (ReasoningEffort is not ("low" or "medium" or "high" or "xhigh" or "max"))
            throw new InvalidOperationException("OpenAI:ReasoningEffort must be low, medium, high, xhigh, or max.");
        if (MaxOutputTokens is < 512 or > 16384) throw new InvalidOperationException("OpenAI:MaxOutputTokens must be between 512 and 16384.");
    }
}
