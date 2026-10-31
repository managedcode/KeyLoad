using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockReadBudgetAssertions
{
    internal static async Task RequireAsync(ChunkSelectedIntegrityFixture fixture, bool healthy)
    {
        var recipe = ChunkMultiblockReadByteRecipe.Capture(fixture, healthy);
        await PhaseAsync(fixture, () => ChunkMultiblockReadInterruptionTrial.RequireAsync(fixture, recipe, healthy, cancelled: true));
        await PhaseAsync(fixture, () => ChunkMultiblockReadInterruptionTrial.RequireAsync(fixture, recipe, healthy, cancelled: false));
        await ChunkMultiblockBoundaryRows.RequireAsync(fixture, healthy);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
    }

    private static async Task PhaseAsync(ChunkSelectedIntegrityFixture fixture, Func<Task> operation)
    {
        var before = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before), failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkSelectedIntegrityAssertions.Replay(fixture), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
