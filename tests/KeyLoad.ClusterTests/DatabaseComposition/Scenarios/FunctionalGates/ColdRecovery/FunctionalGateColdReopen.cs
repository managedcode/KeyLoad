using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateColdReopen
{
    // Each reopen of the same incarnation owns its own restart receipts.
    internal static async Task RunAsync(FunctionalGateFixture fixture, long reopen, CancellationToken token)
    {
        var execution = fixture.ColdRuntime
            ?? throw new InvalidOperationException("The original functional runtime is unavailable.");
        var profile = fixture.Identity;
        var scenario = ProcessRuntimeOwnedReceipt.Scenario(profile.Incarnation, reopen);
        var stopped = await Task.WhenAll(fixture.NodeNames.Select(node => ObserveAsync(
            () => execution.KillAsync(node, scenario, token)))).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny([.. stopped.SelectMany(item => item)]);
        _ = execution.RequireKilledOwners(fixture.App);
        FunctionalGateStorageOwnership.RequireReleased(fixture);
        FunctionalGatePhysicalFileAssertions.RequireStopped(fixture, fixture.PhysicalFiles
            ?? throw new InvalidOperationException(FunctionalGatePhysicalFileProtocol.Failure));
        // Start every original silo before any execution health settlement.
        var begun = await Task.WhenAll(fixture.NodeNames.Select(node => ObserveAsync(async () =>
        {
            _ = await execution.BeginRestartAsync(node, token).ConfigureAwait(false);
        }))).ConfigureAwait(false);
        var settled = await Task.WhenAll(fixture.NodeNames.Select(node => ObserveAsync(
            () => execution.RestartAsync(node, token)))).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny([.. begun.SelectMany(item => item), .. settled.SelectMany(item => item)]);
        await Assert.That(fixture.Identity.Incarnation).IsEqualTo(profile.Incarnation);
    }

    private static async Task<Exception[]> ObserveAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures).ConfigureAwait(false);
        return [.. failures];
    }
}
