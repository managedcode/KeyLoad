using KeyLoad.Server;
using ManagedCode.MCPGateway;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Builds the real package factory and production catalog owner for native graph cases.</summary>
internal sealed class McpGatewayCatalogTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private McpGatewayCatalogTestHost(ServiceProvider services)
    {
        _services = services;
        Owner = new McpGatewayCatalogOwner(
            services.GetRequiredService<ManagedCode.MCPGateway.Abstractions.IMcpGatewayFactory>(),
            services.GetRequiredService<IHttpContextAccessor>());
    }

    internal McpGatewayCatalogOwner Owner { get; }

    internal static McpGatewayCatalogTestHost Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddMcpGateway();
        return new McpGatewayCatalogTestHost(services.BuildServiceProvider());
    }

    public async ValueTask DisposeAsync()
    {
        await Owner.DisposeAsync().ConfigureAwait(false);
        await _services.DisposeAsync().ConfigureAwait(false);
    }
}
