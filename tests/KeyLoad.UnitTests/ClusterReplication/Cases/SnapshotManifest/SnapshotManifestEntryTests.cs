namespace KeyLoad.UnitTests.Features.ClusterReplication;

[NotInParallel]
internal sealed class SnapshotManifestEntryTests
{
    [Test]
    [Arguments(SnapshotManifestEntryProtocol.Manifest)]
    [Arguments(SnapshotManifestEntryProtocol.Image)]
    public async Task ActualRetainedManifestOrPublishedImageFifoRefusesInOriginalChildThenSameTransferInstallsTailReplaysAndTwoColdOwnersRemainHealthy(string variant)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.ShutdownBound, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current!.Execution.CancellationToken);
        await SnapshotManifestEntryFlow.RunAsync(variant, caller.Token);
    }
}
