namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorMap.SerializerAlias)]
internal sealed record EventVectorMap
{
    internal const string SerializerAlias = "keyload.core.event-vector-map.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public Guid MapId { get; init; }

    [Orleans.Id(2)]
    public PartitionRef ControlPartition { get; init; } = null!;

    [Orleans.Id(3)]
    public string PrincipalId { get; init; } = null!;

    [Orleans.Id(4)]
    public long ControlPolicyEpoch { get; init; }

    [Orleans.Id(5)]
    public Guid ControlIncarnation { get; init; }

    [Orleans.Id(6)]
    public long Revision { get; init; }

    [Orleans.Id(7)]
    public long CoverageGeneration { get; init; }

    [Orleans.Id(8)]
    public ReadOnlyMemory<byte> SourceManifestDigest { get; init; }

    [Orleans.Id(9)]
    public int PageCount { get; init; }

    [Orleans.Id(10)]
    public DateTimeOffset ExpiresAt { get; init; }

    [Orleans.Id(11)]
    public bool Released { get; init; }

    [Orleans.Id(12)]
    public long RetainedPayloadBytes { get; init; }

    [Orleans.Id(13)]
    public EventVectorStartPolicy StartPolicy { get; init; }

    [Orleans.Id(14)]
    public Guid OriginalOpenCommandId { get; init; }

    [Orleans.Id(15)]
    public ReadOnlyMemory<byte> OriginalOptionsDigest { get; init; }

    [Orleans.Id(16)]
    public EventVectorMapState State { get; init; }

    [Orleans.Id(17)]
    public Guid? PendingPhaseId { get; init; }

    [Orleans.Id(18)]
    public Guid? LastSettledPhaseId { get; init; }

    [Orleans.Id(19)]
    public ReadOnlyMemory<byte> OriginalSourceManifestDigest { get; init; }
    [Orleans.Id(20)]
    public long CleanupGeneration { get; init; }
}
