namespace KeyLoad.Server;

/// <summary>Initializes the real graph before readiness and drains execution calls during host shutdown.</summary>
internal sealed class McpGatewayCatalogWarmup(McpGatewayCatalogOwner owner) : IHostedService
{
    /// <summary>Fails startup if canonical metadata cannot produce a usable execution graph.</summary>
    public Task StartAsync(CancellationToken cancellationToken) => owner.InitializeAsync(cancellationToken);

    /// <summary>Retains execution owners until every admitted call has settled.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => owner.DisposeAsync().AsTask();
}
