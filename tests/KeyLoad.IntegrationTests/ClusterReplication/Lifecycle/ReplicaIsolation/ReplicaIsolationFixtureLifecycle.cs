namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Owns successful fault-fixture startup while retaining the fixture's original failed-start cleanup contract.</summary>
internal sealed class ReplicaIsolationFixtureLifecycle : IAsyncDisposable
{
    private bool initialized;

    internal ClusterFixture Fixture { get; } = new(ReplicaIsolationProfile.OwnedLinuxNamespace);

    internal async Task InitializeAsync()
    {
        await Fixture.InitializeAsync().ConfigureAwait(false);
        initialized = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (initialized)
        {
            await Fixture.DisposeAsync().ConfigureAwait(false);
            initialized = false;
        }
    }
}
