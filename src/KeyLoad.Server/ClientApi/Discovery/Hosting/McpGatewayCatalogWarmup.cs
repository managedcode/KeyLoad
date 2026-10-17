namespace KeyLoad.Server;

/// <summary>Initializes the real graph before readiness and drains native calls during host shutdown.</summary>
internal sealed class McpGatewayCatalogWarmup(McpGatewayCatalogOwner owner) : IHostedService
{
    /// <summary>Fails startup if canonical metadata cannot produce a usable native graph.</summary>
    public Task StartAsync(CancellationToken cancellationToken) => owner.InitializeAsync(cancellationToken);

    /// <summary>Retains native owners until every admitted call has settled.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => owner.DisposeAsync().AsTask();
}
