using Orleans.TestingHost;
using IAsyncInitializer = TUnit.Core.Interfaces.IAsyncInitializer;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class CqrsClusterFixture : IAsyncInitializer, IAsyncDisposable
{
    private static readonly TimeSpan StartupBound = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownBound = TimeSpan.FromSeconds(30);
    private bool disposed;

    public CqrsClusterFixture()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<CqrsSiloConfigurator>();
        builder.AddClientBuilderConfigurator<CqrsClientConfigurator>();
        Cluster = builder.Build();
    }

    public TestCluster Cluster
    {
        get;
    }

    public IGrainFactory GrainFactory => Cluster.Client;

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(StartupBound, TimeProvider.System);
        var logRoot = Path.Combine(AppContext.BaseDirectory, "logs");
        try
        {
            await Cluster.DeployAsync(deadline.Token);
        }
        catch (Exception startupError)
        {
            await ClusterStartupEvidence.ThrowAfterCleanupAsync(startupError, Cluster,
                nameof(CqrsClusterFixture), logRoot, false, deadline.IsCancellationRequested,
                () => DisposeAsync().AsTask());
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        using var deadline = new CancellationTokenSource(ShutdownBound, TimeProvider.System);
        await CqrsTestSupport.RunWithCleanupAsync(
            () => Cluster.StopAllSilosAsync(deadline.Token),
            async () => await Cluster.DisposeAsync());
        GC.SuppressFinalize(this);
    }
}
