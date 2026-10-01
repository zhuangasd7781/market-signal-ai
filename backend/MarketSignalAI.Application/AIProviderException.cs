using MarketSignalAI.Domain;

namespace MarketSignalAI.Application;

// Safe diagnostic text only: never attach upstream bodies or request credentials.
public sealed class AIProviderException(string message, TokenUsage? usage = null) : Exception(message)
{
    public TokenUsage? Usage { get; } = usage;
}
