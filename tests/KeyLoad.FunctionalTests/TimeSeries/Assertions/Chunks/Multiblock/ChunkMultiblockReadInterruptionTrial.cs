using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockReadInterruptionTrial
{
    internal static async Task RequireAsync(ChunkSelectedIntegrityFixture fixture,
        ChunkMultiblockReadByteRecipe recipe, bool healthy)
    {
        using var cancellation = new CancellationTokenSource();
        var budget = new ReadExecutionBudget(cancellation.Token);
        var before = fixture.Store.GetReadDiagnostics().PointExaminedBytes;
        var triggered = false;
        void Observe()
        {
            if (!triggered && fixture.Store.GetReadDiagnostics().PointExaminedBytes - before >= recipe.FirstBytes)
            {
                triggered = true;
                cancellation.Cancel();
            }
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await ChunkMultiblockReadInterruptionFaults.CancellationAsync(fixture, budget, Observe, cancellation.Token);
            await Assert.That(triggered).IsTrue();
            await Assert.That(fixture.Store.GetReadDiagnostics().PointExaminedBytes - before).IsGreaterThanOrEqualTo(recipe.FirstBytes);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var rows = ChunkMultiblockReadBudgetCalls.Read(fixture, new ReadExecutionBudget());
            await Assert.That(JsonSerializer.Serialize(rows, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(ChunkMultiblockBoundaryData.Expected(healthy), JsonDefaults.Options));
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
