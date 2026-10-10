namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorSourcePhase.SerializerAlias)]
internal sealed record EventVectorSourcePhase
{
    internal const string SerializerAlias = "keyload.core.event-vector-source-phase.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public Guid MapId { get; init; }

    [Orleans.Id(2)]
    public PartitionRef ControlPartition { get; init; } = null!;

    [Orleans.Id(3)]
    public Guid ControlIncarnation { get; init; }

    [Orleans.Id(4)]
    public long CoverageGeneration { get; init; }

    [Orleans.Id(5)]
    public long MapRevision { get; init; }

    [Orleans.Id(6)]
    public Guid PhaseCommandId { get; init; }

    [Orleans.Id(7)]
    public EventVectorSourcePhaseRole Role { get; init; }

    [Orleans.Id(8)]
    public int SourceOrdinal { get; init; }

    [Orleans.Id(9)]
    public EventSourceRef Source { get; init; } = null!;

    [Orleans.Id(10)]
    public PhysicalShardRecord SourceOwner { get; init; } = null!;

    [Orleans.Id(11)]
    public PhysicalShardRecord ControlOwner { get; init; } = null!;

    [Orleans.Id(12)]
    public string PrincipalId { get; init; } = null!;

    [Orleans.Id(13)]
    public long ControlPolicyEpoch { get; init; }

    [Orleans.Id(14)]
    public long SourcePolicyEpoch { get; init; }

    [Orleans.Id(15)]
    public long SourceSchemaVersion { get; init; }

    [Orleans.Id(16)]
    public long OriginalPosition { get; init; }

    [Orleans.Id(17)]
    public long TargetPosition { get; init; }

    [Orleans.Id(18)]
    public ReadOnlyMemory<byte> OriginalNativeBody { get; init; }

    [Orleans.Id(19)]
    public ReadOnlyMemory<byte> OriginalBodyDigest { get; init; }

    [Orleans.Id(20)]
    public string Nonce { get; init; } = null!;

    [Orleans.Id(21)]
    public DateTimeOffset FirstExpiresAt { get; init; }

    [Orleans.Id(22)]
    public OperationResult? SourceOriginalResult { get; init; }

    [Orleans.Id(23)]
    public ReadOnlyMemory<byte> SourceOriginalOutcomeDigest { get; init; }

    [Orleans.Id(24)]
    public ReadOnlyMemory<byte> SourceObservationWitness { get; init; }

    [Orleans.Id(25)]
    public EventVectorSourcePhaseDisposition Disposition { get; init; }

    [Orleans.Id(26)]
    public long SourceLogicalPlacementRevision { get; init; }

    [Orleans.Id(27)]
    public long SourceDirectoryFence { get; init; }
    [Orleans.Id(28)]
    public long CleanupGeneration { get; init; }
}
