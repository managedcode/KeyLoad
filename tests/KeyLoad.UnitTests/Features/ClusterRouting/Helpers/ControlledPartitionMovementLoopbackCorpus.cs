using System.Collections.Immutable;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Binds persisted native owner tuples to actual discovered loopback listeners.</summary>
internal sealed class ControlledPartitionMovementLoopbackCorpus
{
    private const long InitialOwnershipEpoch = 1;

    internal ControlledPartitionMovementLoopbackCorpus(ControlledPartitionMovementLoopbackListeners listeners)
    {
        Control = Entry(PhysicalOwnerDirectoryWholeFlow.Control.Owner, listeners.ControlOrigins);
        Destination = Entry(PhysicalOwnerDirectoryWholeFlow.Destination.Owner, listeners.DestinationOrigins);
        DestinationSiloEndpoints = Enumerable.Range(listeners.ControlOrigins.Length,
            listeners.DestinationOrigins.Length).Select(index => listeners.NativeEndpoint(index).ToString()).ToArray();
    }

    internal RegisteredPhysicalOwnerV1 Control { get; }
    internal RegisteredPhysicalOwnerV1 Destination { get; }
    internal string[] DestinationSiloEndpoints { get; }

    private static RegisteredPhysicalOwnerV1 Entry(PhysicalShardRecord original,
        ImmutableArray<string> actualOrigins)
        => new(new(original.PhysicalShardId, original.Incarnation, actualOrigins, InitialOwnershipEpoch),
            actualOrigins.Select(origin => new Uri(origin).AbsoluteUri).ToImmutableArray());
}
