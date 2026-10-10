namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorSourcePin.SerializerAlias)]
internal sealed record EventVectorSourcePin
{
    internal const string SerializerAlias = "keyload.core.event-vector-source-pin.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public Guid MapId { get; init; }

    [Orleans.Id(2)]
    public PartitionRef ControlPartition { get; init; } = null!;

    [Orleans.Id(3)]
    public Guid ControlIncarnation { get; init; }

    [Orleans.Id(4)]
    public EventSourceRef Source { get; init; } = null!;

    [Orleans.Id(5)]
    public string PrincipalId { get; init; } = null!;

    [Orleans.Id(6)]
    public long SourcePolicyEpoch { get; init; }

    [Orleans.Id(7)]
    public long SourceSchemaVersion { get; init; }

    [Orleans.Id(8)]
    public long Position { get; init; }

    [Orleans.Id(9)]
    public long CoverageGeneration { get; init; }

    [Orleans.Id(10)]
    public long PinRevision { get; init; }

    [Orleans.Id(11)]
    public Guid OriginalPinCommandId { get; init; }

    [Orleans.Id(12)]
    public bool Released { get; init; }

    [Orleans.Id(13)]
    public ReadOnlyMemory<byte> LastObservedControlPhaseDigest { get; init; }
    [Orleans.Id(14)]
    public DateTimeOffset OriginalPinExpiresAt { get; init; }

    [Orleans.Id(15)]
    public ReadOnlyMemory<byte> OriginalPinBodyDigest { get; init; }
}
