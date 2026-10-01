using System.Collections.Concurrent;
using MarketSignalAI.Api;
using MarketSignalAI.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketSignalAI.Tests;

public sealed class WorkerTests
{
    [Fact]
    public async Task Worker_UsesTaipeiSchedule_DeduplicatesTicks_AndRunsNextDay()
    {
        var clock = new TickClock(new DateTimeOffset(2026, 10, 1, 0, 29, 40, TimeSpan.Zero));
        var executor = new RecordingExecutor();
        using var services = new ServiceCollection().AddSingleton<IMarketScheduleExecutor>(executor).BuildServiceProvider();
        using var worker = new MarketAnalysisWorker(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MarketAnalysisWorker>.Instance, clock);
        await worker.StartAsync(default);
        try
        {
            Assert.Empty(executor.Calls);
            await clock.TickAsync(new(2026, 10, 1, 0, 30, 0, TimeSpan.Zero));
            await clock.TickAsync(new(2026, 10, 1, 0, 30, 20, TimeSpan.Zero));
            await clock.TickAsync(new(2026, 10, 1, 0, 30, 40, TimeSpan.Zero));
            Assert.Single(executor.Calls);
            Assert.True(executor.Calls.Single().Opening);
            foreach (var utcHour in new[] { 1, 2, 4, 5 })
                await clock.TickAsync(new(2026, 10, 1, utcHour, 5, 0, TimeSpan.Zero));
            await clock.TickAsync(new(2026, 10, 1, 3, 5, 0, TimeSpan.Zero)); // 11:05 is not scheduled.
            Assert.Equal(5, executor.Calls.Count);
            Assert.All(executor.Calls, call => Assert.Equal(new DateOnly(2026, 10, 1), call.Date));
            Assert.Equal(4, executor.Calls.Count(call => !call.Opening));
            await clock.TickAsync(new(2026, 10, 2, 0, 30, 0, TimeSpan.Zero));
            Assert.Equal(6, executor.Calls.Count);
            Assert.Equal(new DateOnly(2026, 10, 2), executor.Calls.Last().Date);
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task Worker_RetriesFailedSlot_ThenContinuesToNextSlot()
    {
        var clock = new TickClock(new DateTimeOffset(2026, 10, 1, 1, 5, 0, TimeSpan.Zero));
        var executor = new RecordingExecutor { FailFirst = true };
        using var services = new ServiceCollection().AddSingleton<IMarketScheduleExecutor>(executor).BuildServiceProvider();
        using var worker = new MarketAnalysisWorker(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MarketAnalysisWorker>.Instance, clock);
        await worker.StartAsync(default);
        try
        {
            Assert.Single(executor.Calls);
            await clock.TickAsync(new(2026, 10, 1, 1, 5, 20, TimeSpan.Zero));
            await clock.TickAsync(new(2026, 10, 1, 1, 5, 40, TimeSpan.Zero));
            Assert.Equal(2, executor.Calls.Count);
            await clock.TickAsync(new(2026, 10, 1, 2, 5, 0, TimeSpan.Zero));
            Assert.Equal(3, executor.Calls.Count);
        }
        finally { await worker.StopAsync(default); }
    }

    private sealed class RecordingExecutor : IMarketScheduleExecutor
    {
        public ConcurrentQueue<(DateOnly Date, bool Opening)> Calls { get; } = new();
        public bool FailFirst { get; init; }
        public Task ExecuteAsync(DateOnly date, bool isOpeningCheck, CancellationToken ct)
        {
            Calls.Enqueue((date, isOpeningCheck));
            if (FailFirst && Calls.Count == 1) throw new HttpRequestException("Simulated calendar failure");
            return Task.CompletedTask;
        }
    }

    // Only advances when the test requests a tick; no wall-clock schedule waits.
    private sealed class TickClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset now = initial;
        private TickTimer? timer;
        private int reads;
        public override DateTimeOffset GetUtcNow() { Interlocked.Increment(ref reads); return now; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(TimeSpan.FromSeconds(20), period);
            return timer = new TickTimer(callback, state);
        }
        public async Task TickAsync(DateTimeOffset time)
        {
            now = time;
            var before = Volatile.Read(ref reads);
            timer!.Fire();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (Volatile.Read(ref reads) == before) await Task.Delay(1, timeout.Token);
            // Let the synchronous recording executor finish after the clock is read.
            await Task.Delay(10, timeout.Token);
        }
    }
    private sealed class TickTimer(TimerCallback callback, object? state) : ITimer
    {
        public void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}