namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorCoverageCapture.SerializerAlias)]
internal sealed record EventVectorCoverageCapture
{
    internal const string SerializerAlias = "keyload.core.event-vector-coverage-capture.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public Guid MapId { get; init; }

    [Orleans.Id(2)]
    public PartitionRef ControlPartition { get; init; } = null!;

    [Orleans.Id(3)]
    public string PrincipalId { get; init; } = null!;

    [Orleans.Id(4)]
    public EventFeedScope Scope { get; init; } = null!;

    [Orleans.Id(5)]
    public int GroupOrdinal { get; init; }

    [Orleans.Id(6)]
    public PhysicalShardRecord SourceOwner { get; init; } = null!;

    [Orleans.Id(7)]
    public Guid NodeId { get; init; }

    [Orleans.Id(8)]
    public long ReadGeneration { get; init; }

    [Orleans.Id(9)]
    public long StoreCutPosition { get; init; }

    [Orleans.Id(10)]
    public long AppliedCutPosition { get; init; }

    [Orleans.Id(11)]
    public long PolicyEpoch { get; init; }

    [Orleans.Id(12)]
    public ReadOnlyMemory<byte> OriginalEncodedEntries { get; init; }

    [Orleans.Id(13)]
    public ReadOnlyMemory<byte> EntryChecksum { get; init; }

    [Orleans.Id(14)]
    public EventVectorCoverageRow PhysicalCatalog { get; init; } = null!;

    [Orleans.Id(15)]
    public EventVectorCoverageRow[] RegisteredOwnerRows { get; init; } = [];

    [Orleans.Id(16)]
    public EventVectorCoverageRow[] PlacementRows { get; init; } = [];

    [Orleans.Id(17)]
    public EventVectorCoverageRow? LogicalDirectory { get; init; }

    [Orleans.Id(18)]
    public bool HasMore { get; init; }

    [Orleans.Id(19)]
    public EventVectorCoverageRow[] AtomicPartitionRosterRows { get; init; } = [];

    [Orleans.Id(20)]
    public EventVectorCoverageRow[] SourceHeadRows { get; init; } = [];

    [Orleans.Id(21)]
    public EventVectorCoverageRow[] RosterOriginRows { get; init; } = [];

    [Orleans.Id(22)]
    public EventVectorCoverageRow? RosterRestoreIdentity { get; init; }

}
