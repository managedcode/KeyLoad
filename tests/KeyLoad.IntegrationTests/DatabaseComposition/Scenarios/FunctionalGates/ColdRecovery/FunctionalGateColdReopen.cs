using KeyLoad.IntegrationTests.Features.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdReopen
{
    internal static async Task RunAsync(FunctionalGateFixture fixture, CancellationToken token)
    {
        var execution = fixture.ColdRuntime
            ?? throw new InvalidOperationException("The original functional runtime is unavailable.");
        var profile = fixture.Identity;
        var scenario = ProcessRuntimeOwnedReceipt.Scenario(profile.Incarnation, 1);
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
