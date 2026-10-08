using System.Collections.Immutable;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Binds the two original physical identities to actual discovered native listeners.</summary>
internal sealed class ControlledPartitionMovementProcessOwners
{
    private const string ControlIdText = "10213243-5465-7687-98a9-bacbdcedfe0f";
    private const string ControlIncarnationText = "00112233-4455-6677-8899-aabbccddeeff";
    private const string DestinationIdText = "da36582e-c35f-44ec-b0a4-29043539dc5f";
    private const string DestinationIncarnationText = "c5d21e44-c4dc-4b21-924f-90b1521cd5e5";
    private const long InitialEpoch = 1;
    private static readonly Guid ControlId = Guid.Parse(ControlIdText);
    private static readonly Guid ControlIncarnation = Guid.Parse(ControlIncarnationText);
    private static readonly Guid DestinationId = Guid.Parse(DestinationIdText);
    private static readonly Guid DestinationIncarnation = Guid.Parse(DestinationIncarnationText);

    internal ControlledPartitionMovementProcessOwners(ControlledPartitionMovementLoopbackListeners listeners)
    {
        Control = Entry(ControlId, ControlIncarnation, listeners.ControlOrigins);
        Destination = Entry(DestinationId, DestinationIncarnation, listeners.DestinationOrigins);
        DestinationSiloEndpoints = Enumerable.Range(listeners.ControlOrigins.Length,
            listeners.DestinationOrigins.Length).Select(index => listeners.NativeEndpoint(index).ToString()).ToArray();
    }

    internal RegisteredPhysicalOwnerV1 Control { get; }
    internal RegisteredPhysicalOwnerV1 Destination { get; }
    internal string[] DestinationSiloEndpoints { get; }

    private static RegisteredPhysicalOwnerV1 Entry(Guid physicalId, Guid incarnation,
        ImmutableArray<string> actualOrigins)
        => new(new(physicalId, incarnation, actualOrigins, InitialEpoch),
            actualOrigins.Select(origin => new Uri(origin).AbsoluteUri).ToImmutableArray());
}
