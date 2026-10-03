namespace MarketSignalAI.Infrastructure;
public sealed class NewsOptions
{
    public string Model { get; set; } = "deepseek-flash";
    public int MaxInputArticles { get; set; } = 90;
    public int MaxOutputTokens { get; set; } = 8192;
    public int TimeoutSeconds { get; set; } = 180;
    public NewsFeed[] Feeds { get; set; } = [
        new("Federal Reserve", "MACRO", "https://www.federalreserve.gov/feeds/press_all.xml", "OFFICIAL"),
        new("Federal Reserve", "MACRO", "https://www.federalreserve.gov/feeds/speeches.xml", "OFFICIAL"),
        new("CNBC Economy", "MACRO", "https://www.cnbc.com/id/20910258/device/rss/rss.html", "FINANCIAL_MEDIA"),
        new("CNBC Technology", "TECH", "https://www.cnbc.com/id/19854910/device/rss/rss.html", "FINANCIAL_MEDIA"),
        new("NVIDIA", "TECH", "https://nvidianews.nvidia.com/releases.xml", "OFFICIAL"),
        new("中央社", "TAIWAN", "https://feeds.feedburner.com/rsscna/finance", "NEWS_AGENCY"),
        new("中央社科技", "TAIWAN", "https://feeds.feedburner.com/rsscna/technology", "NEWS_AGENCY")
    ];
}
public sealed record NewsFeed(string Publisher, string Topic, string Url, string SourceType);
