namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorSourceMutation.SerializerAlias)]
internal sealed record EventVectorSourceMutation
{
    internal const string SerializerAlias = "keyload.core.event-vector-source-mutation.v1";

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
    public PhysicalShardRecord SourceOwner { get; init; } = null!;

    [Orleans.Id(7)]
    public PhysicalShardRecord ControlOwner { get; init; } = null!;

    [Orleans.Id(8)]
    public EventVectorSourcePhaseRole Role { get; init; }

    [Orleans.Id(9)]
    public long ExpectedPinRevision { get; init; }

    [Orleans.Id(10)]
    public long Position { get; init; }

    [Orleans.Id(11)]
    public long CoverageGeneration { get; init; }

    [Orleans.Id(12)]
    public long SourcePolicyEpoch { get; init; }

    [Orleans.Id(13)]
    public long SourceSchemaVersion { get; init; }

    [Orleans.Id(14)]
    public long LogicalPlacementRevision { get; init; }

    [Orleans.Id(15)]
    public long DirectoryFence { get; init; }

    [Orleans.Id(16)]
    public string Nonce { get; init; } = null!;

    [Orleans.Id(17)]
    public DateTimeOffset FirstExpiresAt { get; init; }
    [Orleans.Id(18)]
    public Guid OriginalPinCommandId { get; init; }

    [Orleans.Id(19)]
    public ReadOnlyMemory<byte> OriginalPinNativeBody { get; init; }
    [Orleans.Id(20)]
    public long ExpectedCoverageGeneration { get; init; }
    [Orleans.Id(21)]
    public long CleanupGeneration { get; init; }
}
