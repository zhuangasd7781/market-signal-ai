using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using Microsoft.Extensions.Logging;
namespace MarketSignalAI.Infrastructure;

public sealed class RssNewsSearchProvider(HttpClient http, NewsOptions options, ILogger<RssNewsSearchProvider> logger) : INewsSearchProvider
{
    public async Task<NewsSearchBatch> SearchAsync(NewsSearchQuery query, CancellationToken ct)
    {
        var results = new List<NewsSearchResult>();var coverage = new List<NewsSourceCoverage>();
        foreach(var feed in options.Feeds.Where(x=>query.Topics.Contains(x.Topic)))
        {
            logger.LogInformation("News search topic {Topic} publisher {Publisher}",feed.Topic,feed.Publisher);
            try
            {
                if(!Uri.TryCreate(feed.Url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.UserInfo.Length>0)
                    throw new InvalidDataException("Invalid configured feed URL.");
                using var response=await http.GetAsync(uri,ct);response.EnsureSuccessStatusCode();
                await using var stream=await response.Content.ReadAsStreamAsync(ct);
                using var reader=XmlReader.Create(stream,new XmlReaderSettings { Async=true,DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2_000_000 });
                var doc=await XDocument.LoadAsync(reader,LoadOptions.None,ct);
                var items=doc.Descendants().Where(x=>x.Name.LocalName is "item" or "entry").Take(100).ToArray();
                if(doc.Root?.Name.LocalName is not ("rss" or "feed" or "RDF")) throw new InvalidDataException("Feed format not recognized.");
                var parsed=items.Select(x=>Parse(x,feed)).Where(x=>x is not null).Cast<NewsSearchResult>().ToArray();
                results.AddRange(parsed);coverage.Add(new(feed.Publisher,feed.Topic,"AVAILABLE",parsed.Length,"RSS only covers the publisher's recent feed items; not an exhaustive 72-hour web search."));
                logger.LogInformation("News source results {Publisher} {ResultCount}",feed.Publisher,parsed.Length);
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch(Exception ex)
            {
                var error=ex is HttpRequestException {StatusCode:{} code} ? $"HTTP {(int)code}" : $"Feed unavailable ({ex.GetType().Name}).";
                coverage.Add(new(feed.Publisher,feed.Topic,"UNAVAILABLE",0,error));
                logger.LogWarning("News source unavailable {Publisher} {Topic} {Error}",feed.Publisher,feed.Topic,error);
            }
        }
        if(coverage.Count==0 || coverage.All(x=>x.Status=="UNAVAILABLE")) throw new NewsPipelineException("SEARCH","所有新聞來源均無法取得，未建立市場情報。");
        return new("FIXED_RSS",results,coverage);
    }
    public static NewsSearchResult? Parse(XElement item,NewsFeed feed)
    {
        string? Field(params string[] names)=>item.Elements().FirstOrDefault(x=>names.Contains(x.Name.LocalName))?.Value;
        var title=Clean(Field("title"),300);
        var link=item.Elements().FirstOrDefault(x=>x.Name.LocalName=="link" && (x.Attribute("rel")?.Value is null or "alternate"));
        var url=link?.Attribute("href")?.Value ?? link?.Value;
        if(title.Length==0 || !Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length>0) return null;
        var builder=new UriBuilder(uri){Fragment=""};url=builder.Uri.AbsoluteUri;
        var text=Field("pubDate","published","date"); // updated is NOT publication time.
        DateTimeOffset? published=null;
        if(text is not null && Regex.IsMatch(text.Trim(),@"(?:GMT|UTC|Z|[+-]\d{2}:?\d{2})$",RegexOptions.IgnoreCase) &&
            DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out var date)) published=date.ToUniversalTime();
        var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant()[..24];
        return new(id,feed.Topic,title,url,feed.Publisher,published,Clean(Field("description","summary","encoded","content"),900),feed.SourceType,published.HasValue ? "VERIFIED" : "UNKNOWN");
    }
    private static string Clean(string? text,int limit)
    {
        var value=WebUtility.HtmlDecode(Regex.Replace(text ?? "","<[^>]*>"," "));
        value=Regex.Replace(value,@"\s+"," ").Trim();return value[..Math.Min(value.Length,limit)];
    }
}
