using FastEndpoints;
using FastEndpoints.Swagger;
using MarketSignalAI.Application;
using MarketSignalAI.Infrastructure;
using MarketSignalAI.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
if (!builder.Environment.IsDevelopment() || !builder.Configuration.GetValue<bool>("Demo:Enabled"))
    throw new InvalidOperationException("Phase 1 requires Development environment and Demo:Enabled=true. Production authentication is not implemented.");

builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument(o => o.DocumentSettings = s => s.OperationProcessors.Add(new DemoMutationHeaderProcessor()));
builder.Services.AddSingleton<ICurrentUser, DemoCurrentUser>();
builder.Services.AddScoped<SignalService>();
builder.Services.AddScoped<AIProviderSettingsService>();
builder.Services.AddScoped<PromptService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IMarketDataProvider, YahooMarketDataProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MarketSignalAI/1.0");
});
builder.Services.AddScoped<IMarketAnalysisRunner, MarketAnalysisRunner>();
builder.Services.AddScoped<IMarketScheduleExecutor, MarketScheduleExecutor>();
var openAI = builder.Configuration.GetSection("OpenAI").Get<OpenAIAnalystOptions>() ?? new();
if (string.IsNullOrWhiteSpace(openAI.ApiKey)) openAI.ApiKey = builder.Configuration["OPENAI_API_KEY"] ?? "";
builder.Services.AddSingleton(openAI);
{
    builder.Services.AddHttpClient<OpenAIAnalyst>(client => client.Timeout = Timeout.InfiniteTimeSpan);
    builder.Services.AddTransient<IAIAnalyst>(sp => sp.GetRequiredService<OpenAIAnalyst>());
}

var deepSeek = builder.Configuration.GetSection("DeepSeek").Get<DeepSeekAnalystOptions>() ?? new();
builder.Services.AddSingleton(deepSeek);
{
    builder.Services.AddHttpClient<DeepSeekAnalyst>(client => client.Timeout = Timeout.InfiniteTimeSpan);
    builder.Services.AddTransient<IAIAnalyst>(sp => sp.GetRequiredService<DeepSeekAnalyst>());
}

builder.Services.AddSingleton<IAIAnalyst>(new MockAIAnalyst("claude"));
var providerDefaults = new AIProviderSetting[] { new("openai",openAI.Enabled,openAI.Model),new("deepseek",deepSeek.Enabled,deepSeek.Model),new("claude",false,"mock-v1") };
var storage = builder.Configuration["Storage:Provider"] ?? "Memory";
if (storage.Equals("MySql", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("MySql")
        ?? throw new InvalidOperationException("ConnectionStrings:MySql is required.");
    builder.Services.AddSingleton(new MySqlPromptStore(connectionString));
    builder.Services.AddSingleton<IPromptStore>(sp => sp.GetRequiredService<MySqlPromptStore>());
    builder.Services.AddSingleton(new MySqlAIProviderSettingsStore(connectionString,providerDefaults));
    builder.Services.AddSingleton<IAIProviderSettingsStore>(sp=>sp.GetRequiredService<MySqlAIProviderSettingsStore>());
    builder.Services.AddSingleton(new MySqlSignalStore(connectionString));
    builder.Services.AddSingleton<ISignalStore>(sp => sp.GetRequiredService<MySqlSignalStore>());
    builder.Services.AddSingleton(new MySqlMarketReferenceStore(connectionString));
    builder.Services.AddSingleton<IMarketReferenceStore>(sp => sp.GetRequiredService<MySqlMarketReferenceStore>());
    builder.Services.AddSingleton(new MySqlMarketStore(connectionString));
    builder.Services.AddSingleton<IMarketStore>(sp => sp.GetRequiredService<MySqlMarketStore>());
    if (builder.Configuration.GetValue("MarketWorker:Enabled", true)) builder.Services.AddHostedService<MarketAnalysisWorker>();
}
else if (storage.Equals("Memory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IPromptStore, MemoryPromptStore>();
    builder.Services.AddSingleton<IAIProviderSettingsStore>(new MemoryAIProviderSettingsStore(providerDefaults));
    builder.Services.AddSingleton<MemorySignalStore>();
    builder.Services.AddSingleton<ISignalStore>(sp => sp.GetRequiredService<MemorySignalStore>());
    builder.Services.AddSingleton<IMarketStore, MemoryMarketStore>();
    builder.Services.AddSingleton<IMarketReferenceStore, MemoryMarketReferenceStore>();
}
else throw new InvalidOperationException("Storage:Provider must be Memory or MySql.");

var app = builder.Build();
if (app.Services.GetService<MySqlMarketStore>() is { } marketSql)
    await marketSql.MigrateAsync(app.Lifetime.ApplicationStopping);
if (app.Services.GetService<MySqlSignalStore>() is { } mysql)
    await mysql.SeedAsync(app.Lifetime.ApplicationStopping);
if (app.Services.GetService<MySqlMarketReferenceStore>() is { } referenceSql)
{
    await referenceSql.MigrateAsync(app.Lifetime.ApplicationStopping);
    await referenceSql.SeedAsync(app.Lifetime.ApplicationStopping);
}
if(app.Services.GetService<MySqlAIProviderSettingsStore>() is {} providerSql)
    await providerSql.MigrateAndSeedAsync(app.Lifetime.ApplicationStopping);
if (app.Services.GetService<MySqlPromptStore>() is { } promptSql)
    await promptSql.MigrateAsync(app.Lifetime.ApplicationStopping);
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    // Same-origin browser requests only; JSON mutations are also rejected without the custom header.
    if (context.Request.Method is "POST" or "PUT" or "DELETE" && context.Request.Headers["X-Market-Signal"] != "web")
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsJsonAsync(new { message = "Missing request header." });
        return;
    }
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception ex)
    {
        var status = ex switch { KeyNotFoundException => 404, ArgumentException => 400, InvalidOperationException => 409, _ => 503 };
        if (status == 503) app.Logger.LogError(ex, "Request failed {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { message = status == 503 ? "服務暫時無法使用，請稍後再試。" : ex.Message });
    }
});
app.UseFastEndpoints();
if (app.Environment.IsDevelopment()) app.UseSwaggerGen();
app.MapGet("/health", async (ISignalStore store, CancellationToken ct) =>
    await store.IsHealthyAsync(ct) ? Results.Ok(new { status = "healthy", mode = "demo", storage }) : Results.StatusCode(503));
app.Run();

public partial class Program;

namespace MarketSignalAI.Api
{
    public sealed class DemoCurrentUser : ICurrentUser { public long UserId => 1; }
}
