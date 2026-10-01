namespace MarketSignalAI.Infrastructure;

public sealed class DeepSeekAnalystOptions
{
    public bool Enabled { get; set; }
    public int TimeoutSeconds { get; set; } = 180;
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.deepseek.com/";
    public int MaxOutputTokens { get; set; } = 8192;
    public void Validate()
    {
        if (TimeoutSeconds is < 1 or > 600) throw new InvalidOperationException("DeepSeek:TimeoutSeconds must be between 1 and 600.");
        if (string.IsNullOrWhiteSpace(ApiKey)) throw new InvalidOperationException("DeepSeek:ApiKey is missing.");
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 100 || Model.StartsWith("mock", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DeepSeek:Model must be a real model ID of at most 100 characters.");
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("DeepSeek:BaseUrl must be an HTTPS URL without credentials, query or fragment.");
        if (MaxOutputTokens is < 512 or > 16384) throw new InvalidOperationException("DeepSeek:MaxOutputTokens must be between 512 and 16384.");
    }
}
