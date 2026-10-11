namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorSourceResult.SerializerAlias)]
internal sealed record EventVectorSourceResult
{
    internal const string SerializerAlias = "keyload.core.event-vector-source-result.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public Guid CommandId { get; init; }

    [Orleans.Id(2)]
    public EventVectorSourcePin Pin { get; init; } = null!;

    [Orleans.Id(3)]
    public CommitToken Token { get; init; } = null!;

    [Orleans.Id(4)]
    public DurabilityProfile Durability { get; init; }
}
