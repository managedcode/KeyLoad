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
        clock.Arm(() => budget.ReadBytes == recipe.FirstBytes, afterWork);
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
                await Assert.That(budget.ReadBytes).IsEqualTo(recipe.FirstBytes);
            }, failures);
        }
        finally
        {
            clock.Disarm();
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var following = ChunkMultiblockReadBudgetCalls.Budget(fixture, recipe.TotalBytes);
            var rows = ChunkMultiblockReadBudgetCalls.Read(fixture, following);
            await Assert.That(following.ReadBytes).IsEqualTo(recipe.TotalBytes);
            await Assert.That(JsonSerializer.Serialize(rows, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(ChunkMultiblockBoundaryData.Expected(healthy), JsonDefaults.Options));
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
