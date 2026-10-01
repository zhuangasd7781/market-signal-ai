using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MarketSignalAI.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketSignalAI.Tests;

public sealed class ScheduleTests
{
    [Fact]
    public async Task ClosedDay_PersistsStatus_AndSkipsScheduledAnalysis()
    {
        var signalStore = new MemorySignalStore();
        var store = new MemoryMarketStore(signalStore);
        var market = new StubMarket { IsOpen = false };
        var runner = new StubRunner();
        var executor = new MarketScheduleExecutor(market, store, runner, TimeProvider.System, NullLogger<MarketScheduleExecutor>.Instance);
        var date = new DateOnly(2026, 10, 1);
        await executor.ExecuteAsync(date, true, default);
        await executor.ExecuteAsync(date, false, default);
        Assert.Equal("CLOSED", (await store.GetTradingDayAsync(date, "TW", default))!.Status);
        Assert.Equal(1, market.CalendarCalls);
        Assert.Equal(0, market.SnapshotCalls);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task ScheduledRun_RejectsStaleQuoteBeforeSaving()
    {
        var signalStore = new MemorySignalStore();
        var store = new MemoryMarketStore(signalStore);
        var market = new StubMarket();
        var runner = new MarketAnalysisRunner(market, store, signalStore, [new MockAIAnalyst("openai")], NullLogger<MarketAnalysisRunner>.Instance);
        await Assert.ThrowsAsync<InvalidDataException>(() => runner.RunProductAsync("00631L", default, new DateOnly(2026, 10, 1)));
        Assert.Null(await store.GetLatestSnapshotAsync(1, default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenDay_ChecksCalendarOnce_AndPassesTradeDate(bool openingCheckFirst)
    {
        var store = new MemoryMarketStore(new MemorySignalStore());
        var market = new StubMarket();
        var runner = new StubRunner();
        var executor = new MarketScheduleExecutor(market, store, runner, TimeProvider.System, NullLogger<MarketScheduleExecutor>.Instance);
        var date = new DateOnly(2026, 10, 1);
        if (openingCheckFirst)
        {
            await executor.ExecuteAsync(date, true, default);
            Assert.Equal(0, runner.Calls);
        }
        await executor.ExecuteAsync(date, false, default);
        await executor.ExecuteAsync(date, false, default);
        Assert.Equal("OPEN", (await store.GetTradingDayAsync(date, "TW", default))!.Status);
        Assert.Equal(1, market.CalendarCalls);
        Assert.Equal(2, runner.Calls);
        Assert.Equal(date, runner.ExpectedTradeDate);
    }

    [Fact]
    public async Task CalendarFailure_DoesNotSaveStatusOrRunAnalysis()
    {
        var store = new MemoryMarketStore(new MemorySignalStore());
        var runner = new StubRunner();
        var executor = new MarketScheduleExecutor(new StubMarket { FailCalendar = true }, store, runner,
            TimeProvider.System, NullLogger<MarketScheduleExecutor>.Instance);
        var date = new DateOnly(2026, 10, 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => executor.ExecuteAsync(date, false, default));
        Assert.Null(await store.GetTradingDayAsync(date, "TW", default));
        Assert.Equal(0, runner.Calls);
    }

    private sealed class StubMarket : IMarketDataProvider
    {
        public bool IsOpen { get; init; } = true;
        public bool FailCalendar { get; init; }
        public int CalendarCalls { get; private set; }
        public int SnapshotCalls { get; private set; }
        public Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct) { CalendarCalls++; if (FailCalendar) throw new InvalidDataException("Calendar unavailable"); return Task.FromResult(IsOpen); }
        public Task<MarketSnapshot> GetSnapshotAsync(string symbol, CancellationToken ct)
        {
            SnapshotCalls++;
            return Task.FromResult(new MarketSnapshot(symbol, 10, 10, 11, 9, 9, 100,
                new DateTimeOffset(2026, 9, 30, 5, 30, 0, TimeSpan.Zero), DateTimeOffset.UtcNow));
        }
        public Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<HistoricalPrice>>([]);
    }
    private sealed class StubRunner : IMarketAnalysisRunner
    {
        public int Calls { get; private set; }
        public DateOnly? ExpectedTradeDate { get; private set; }
        public Task<ProductRunResult> RunProductAsync(string symbol, CancellationToken ct, DateOnly? expectedTradeDate = null) => throw new NotImplementedException();
        public Task<BatchRunResult> RunAllAsync(CancellationToken ct, DateOnly? expectedTradeDate = null)
        { Calls++; ExpectedTradeDate = expectedTradeDate; return Task.FromResult(new BatchRunResult([], [])); }
    }
}
