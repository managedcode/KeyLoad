using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockReadInterruptionTrial
{
    private const int PastDeadlineSeconds = 2;
    private const int CallerLifetimeSeconds = 1;

    internal static async Task RequireAsync(ChunkSelectedIntegrityFixture fixture,
        ChunkMultiblockReadByteRecipe recipe, bool healthy, bool cancelled)
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new SampleRollupWorkClock();
        var budget = new ReadExecutionBudget(clock, cancellation.Token);
        budget.ConstrainLifetime(clock.GetUtcNow().AddSeconds(CallerLifetimeSeconds));
        Action afterWork = cancelled ? cancellation.Cancel : () => clock.Advance(TimeSpan.FromSeconds(PastDeadlineSeconds));
        var start = fixture.Store.GetReadDiagnostics().PointExaminedBytes;
        clock.Arm(() => fixture.Store.GetReadDiagnostics().PointExaminedBytes - start >= recipe.FirstBytes, afterWork);
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                if (cancelled)
                {
                    await ChunkMultiblockReadInterruptionFaults.CancellationAsync(fixture, budget, cancellation.Token);
                }
                else
                {
                    await ChunkMultiblockReadInterruptionFaults.DeadlineAsync(fixture, budget);
                }
                await Assert.That(clock.Triggered).IsTrue();
            }, failures);
        }
        finally
        {
            clock.Disarm();
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var rows = ChunkMultiblockReadBudgetCalls.Read(fixture, ChunkMultiblockReadBudgetCalls.Budget(fixture));
            await Assert.That(JsonSerializer.Serialize(rows, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(ChunkMultiblockBoundaryData.Expected(healthy), JsonDefaults.Options));
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
