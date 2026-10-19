using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProtocol.ManifestAlias)]
internal sealed record AnnManifest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string GenerationLeaf,
    [property: global::Orleans.Id(2)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(3)] long IndexGeneration,
    [property: global::Orleans.Id(4)] string PrincipalId,
    [property: global::Orleans.Id(5)] string Collection,
    [property: global::Orleans.Id(6)] string Field,
    [property: global::Orleans.Id(7)] VectorSpace Space,
    [property: global::Orleans.Id(8)] PhysicalShardRecord Placement,
    [property: global::Orleans.Id(9)] AnnSourceCut Source,
    [property: global::Orleans.Id(10)] PackedAnnOptions Policy,
    [property: global::Orleans.Id(11)] int Count,
    [property: global::Orleans.Id(12)] byte[] IndexSha256,
    [property: global::Orleans.Id(13)] CommitProjectionBatchRequest? CheckpointIntent)
{
    [global::Orleans.Id(14)]
    public bool IsPending
    {
        get; init;
    }
    [global::Orleans.Id(15)]
    public long ReplayAfter
    {
        get; init;
    }
    [global::Orleans.Id(16)]
    public long ReplayThrough
    {
        get; init;
    }
    [global::Orleans.Id(17)]
    public string? ReplayCorpusSha256
    {
        get; init;
    }
    [global::Orleans.Id(18)]
    public bool ExplicitBuildStage
    {
        get; init;
    }
}
