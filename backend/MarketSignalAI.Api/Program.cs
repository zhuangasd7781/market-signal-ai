using FastEndpoints;
using MarketSignalAI.Application;
using MarketSignalAI.Infrastructure;
using MarketSignalAI.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
if (!builder.Environment.IsDevelopment() || !builder.Configuration.GetValue<bool>("Demo:Enabled"))
    throw new InvalidOperationException("Phase 1 requires Development environment and Demo:Enabled=true. Production authentication is not implemented.");

builder.Services.AddFastEndpoints();
builder.Services.AddSingleton<ICurrentUser, DemoCurrentUser>();
builder.Services.AddScoped<SignalService>();
var storage = builder.Configuration["Storage:Provider"] ?? "Memory";
if (storage.Equals("MySql", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("MySql")
        ?? throw new InvalidOperationException("ConnectionStrings:MySql is required.");
    builder.Services.AddSingleton(new MySqlSignalStore(connectionString));
    builder.Services.AddSingleton<ISignalStore>(sp => sp.GetRequiredService<MySqlSignalStore>());
}
else if (storage.Equals("Memory", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<ISignalStore, MemorySignalStore>();
else throw new InvalidOperationException("Storage:Provider must be Memory or MySql.");

var app = builder.Build();
if (app.Services.GetService<MySqlSignalStore>() is { } mysql)
    await mysql.SeedAsync(app.Lifetime.ApplicationStopping);
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
app.MapGet("/health", async (ISignalStore store, CancellationToken ct) =>
    await store.IsHealthyAsync(ct) ? Results.Ok(new { status = "healthy", mode = "demo", storage }) : Results.StatusCode(503));
app.Run();

public partial class Program;

namespace MarketSignalAI.Api
{
    public sealed class DemoCurrentUser : ICurrentUser { public long UserId => 1; }
}
