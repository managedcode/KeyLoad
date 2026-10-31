using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockReadInterruptionTrial
{
    internal static async Task RequireAsync(ChunkSelectedIntegrityFixture fixture,
        ChunkMultiblockReadByteRecipe recipe, bool healthy)
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new SampleRollupWorkClock();
        var budget = new ReadExecutionBudget(clock, cancellation.Token);
        var start = fixture.Store.GetReadDiagnostics().PointExaminedBytes;
        clock.Arm(() => fixture.Store.GetReadDiagnostics().PointExaminedBytes - start >= recipe.FirstBytes, cancellation.Cancel);
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await ChunkMultiblockReadInterruptionFaults.CancellationAsync(fixture, budget, cancellation.Token);
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
