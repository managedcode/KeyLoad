namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorSourceReleaseResult.SerializerAlias)]
internal sealed record EventVectorSourceReleaseResult
{
    internal const string SerializerAlias = "keyload.core.event-vector-source-release-result.v1";
    [Orleans.Id(0)]
    public int Version
    {
        get; init;
    }
    [Orleans.Id(1)]
    public Guid CommandId
    {
        get; init;
    }
    [Orleans.Id(2)] public EventSourceRef Source { get; init; } = null!;
    [Orleans.Id(3)]
    public Guid OriginalPinCommandId
    {
        get; init;
    }
    [Orleans.Id(4)]
    public EventVectorReleaseDisposition Disposition
    {
        get; init;
    }
    [Orleans.Id(5)]
    public EventVectorSourcePin? RetainedPin
    {
        get; init;
    }
    [Orleans.Id(6)] public CommitToken Token { get; init; } = null!;
    [Orleans.Id(7)]
    public DurabilityProfile Durability
    {
        get; init;
    }
    [Orleans.Id(8)]
    public ReadOnlyMemory<byte> OriginalOutcomeDigest
    {
        get; init;
    }
}
