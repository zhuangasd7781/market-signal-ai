using System.Text.Json;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Infrastructure;

public static class DemoData
{
    public static readonly User User = new(1, "Demo 投資人", "demo@example.invalid");
    public static readonly Product[] Products = [
        new(1, "00631L", "元大台灣50正2", "TW", "ETF", true, "張", 1000),
        new(2, "5871", "中租-KY", "TW", "Stock", false, "張", 1000),
        new(3, "2330", "台積電", "TW", "Stock", false, "張", 1000),
        new(4, "0050", "元大台灣50", "TW", "ETF", false, "張", 1000),
        new(5, "AAPL", "Apple", "US", "Stock", false, "股", 1)];
    public static readonly AIProvider[] Providers = [new(1, "deepseek", "DeepSeek", 1), new(2, "openai", "GPT", 2), new(3, "claude", "Claude", 3)];

    public static AnalysisRecord[] History(DateTime now)
    {
        var records = new List<AnalysisRecord>();
        foreach (var product in Products.Take(3))
        foreach (var provider in Providers)
        {
            var action = MockAction(product, provider.Code);
            foreach (var previous in new[] { true, false })
            {
                var result = Result(previous ? "HOLD" : action, product.IsLeveraged);
                var input = JsonSerializer.Serialize(new { isMock = true, product, position = (object?)null,
                    marketData = (object?)null, previousDecision = previous ? null : "HOLD" });
                records.Add(new(records.Count + 1, 1, product.Id, provider.Id, "mock-v1", result,
                    input, JsonSerializer.Serialize(result), now.AddMinutes(previous ? -1440 : -product.Id * 12)));
            }
        }
        return records.ToArray();
    }

    public static string MockAction(Product product, string providerCode) => (product.Id, providerCode) switch
    {
        (1, "openai") or (3, "deepseek") or (3, "openai") => "REDUCE",
        (2, "deepseek") => "ADD",
        _ => "HOLD"
    };
    public static Analysis MockResult(string action, bool leveraged) => Result(action, leveraged);
    private static Analysis Result(string action, bool leveraged) => new(action, action is "ADD" or "REDUCE" ? 1 : null,
        60, new("UNKNOWN", "UNVERIFIED", "示範情境：未使用行情或新聞推論，無已確認的 Root Event。"),
        "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN",
        ["此訊號用於展示各 AI 的獨立觀點，不代表實際分析。", "正式分析需結合市場資料、持倉與前次決策。"],
        leveraged ? ["槓桿 ETF 須額外考量波動、回撤與盤整耗損。", "示範資料不能用於交易。"] : ["示範訊號未依行情產生。", "示範資料不能用於交易。"],
        ["情境示例：若正面催化因素獲得證實且趨勢延續，可支持持有。"],
        ["情境示例：若核心投資論點失效，需重新評估風險。"],
        "情境示例：核心催化因素被否定，或趨勢與成交量共同轉弱。",
        [new("等待真實 AI 分析與已確認證據後重新分析。", "HOLD", null)]);
}
