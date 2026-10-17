using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Owns the original suspended AppHost factory until its successful native application handoff.</summary>
internal sealed class ReplicaIsolationBuilderOwnership(IDistributedApplicationTestingBuilder builder) : IAsyncDisposable
{
    private IDistributedApplicationTestingBuilder? pending = builder;

    internal async Task<DistributedApplication> BuildAsync(CancellationToken cancellationToken)
    {
        var application = await (pending ?? throw new InvalidOperationException("The original fault builder was already handed off."))
            .BuildAsync(cancellationToken).ConfigureAwait(false);
        pending = null;
        return application;
    }

    public async ValueTask DisposeAsync()
    {
        if (pending is not null)
        {
            await pending.DisposeAsync().ConfigureAwait(false);
            pending = null;
        }
    }
}
