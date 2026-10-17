namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorToken.SerializerAlias)]
internal sealed record EventVectorToken
{
    internal const string SerializerAlias = "keyload.core.event-vector-token.v1";

    [Orleans.Id(0)]
    public int Version
    {
        get; init;
    }

    [Orleans.Id(1)]
    public string Purpose { get; init; } = null!;

    [Orleans.Id(2)]
    public Guid MapId
    {
        get; init;
    }

    [Orleans.Id(3)]
    public PartitionRef ControlPartition { get; init; } = null!;

    [Orleans.Id(4)]
    public long MapRevision
    {
        get; init;
    }

    [Orleans.Id(5)]
    public long CoverageGeneration
    {
        get; init;
    }

    [Orleans.Id(6)]
    public ReadOnlyMemory<byte> ManifestDigest
    {
        get; init;
    }

    [Orleans.Id(7)]
    public string PrincipalId { get; init; } = null!;

    [Orleans.Id(8)]
    public long ControlPolicyEpoch
    {
        get; init;
    }

    [Orleans.Id(9)]
    public Guid ControlIncarnation
    {
        get; init;
    }

    [Orleans.Id(10)]
    public DateTimeOffset ExpiresAt
    {
        get; init;
    }
}
