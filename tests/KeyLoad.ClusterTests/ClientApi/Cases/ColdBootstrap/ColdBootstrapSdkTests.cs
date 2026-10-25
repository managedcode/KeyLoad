using KeyLoad.Server;

namespace KeyLoad.ClusterTests.ClientApi;

[NotInParallel]
internal sealed class ColdBootstrapSdkTests
{
    [Test]
    public async Task FreshOwnedClusterBootstrapCliAndPersistedSdkRetainExactCommandIdentity()
    {
        ClusterFixture? fixture = null;
        string? root = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                fixture = new ClusterFixture();
                await ServerFailureObserver.ObserveAsync(() => InitializeAndVerifyAsync(fixture), failures)
                    .ConfigureAwait(false);
                root = fixture.ColdStartObservation?.Root;
            }
            finally
            {
                if (fixture is not null)
                {
                    await fixture.DisposeAsync().ConfigureAwait(false);
                }
            }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(root is not null && !Directory.Exists(root) && !File.Exists(root)).IsTrue();
    }

    private static async Task InitializeAndVerifyAsync(ClusterFixture fixture)
    {
        await fixture.InitializeAsync().ConfigureAwait(false);
        var cold = fixture.ColdStartObservation
            ?? throw new InvalidOperationException("The selected execution fixture cold-start observation was absent.");
        await Assert.That(cold.DirectoryExisted || cold.FileExisted).IsFalse();
        await Assert.That(fixture.Root).IsEqualTo(cold.Root);
        await ColdBootstrapSdkFlow.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken)
            .ConfigureAwait(false);
    }
}
