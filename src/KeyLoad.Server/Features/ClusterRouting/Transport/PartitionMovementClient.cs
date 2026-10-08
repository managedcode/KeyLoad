using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Coordinates one original phase through its owned configured transport without retries.</summary>
internal sealed partial class PartitionMovementClient : IPartitionMovementDispatcher, IDisposable
{
    private const int FirstVoter = 0;
    private const int MinimumBodyBytes = 1;
    private readonly NodeOptions options;
    private readonly OrleansNode node;
    private readonly PartitionHost partition;
    private readonly TimeProvider clock;
    private readonly PartitionMovementTransportOwner transport;

    internal PartitionMovementClient(IOptions<NodeOptions> options, OrleansNode node, PartitionHost partition,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock)
    {
        this.options = options.Value;
        this.node = node;
        this.partition = partition;
        this.clock = clock;
        transport = new(options, partition, membership, clock);
    }

    public void Dispose() => transport.Dispose();
}
