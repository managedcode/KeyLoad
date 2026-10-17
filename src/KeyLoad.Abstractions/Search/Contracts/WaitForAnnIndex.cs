namespace KeyLoad;

/// <summary>Requires an explicitly provisioned native ANN generation to cover an acknowledged minimum prefix.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(WaitForAnnIndexProtocol.RequestAlias)]
public sealed record WaitForAnnIndexRequest(
    [property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Collection,
    [property: Orleans.Id(2)] string VectorField,
    [property: Orleans.Id(3)] VectorSpace Space,
    [property: Orleans.Id(4)] ProjectionConsumerRef Consumer,
    [property: Orleans.Id(5)] long IndexGeneration,
    [property: Orleans.Id(6)] CommitToken MinimumToken);

/// <summary>Reports the acquired native generation's actual indexed prefix under current authorization.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(WaitForAnnIndexProtocol.ResultAlias)]
public sealed record WaitForAnnIndexResult(
    [property: Orleans.Id(0)] CommitToken IndexedToken,
    [property: Orleans.Id(1)] long IndexGeneration,
    [property: Orleans.Id(2)] long SchemaVersion,
    [property: Orleans.Id(3)] long PolicyEpoch);

/// <summary>Stable readonly SDK, official MCP and shared SQL CALL identities.</summary>
public static class WaitForAnnIndexProtocol
{
    /// <summary>The authenticated operation route.</summary>
    public const string Route = "/v1/search/ann/wait-for-index";
    /// <summary>The discovered MCP and Q1 CALL operation name.</summary>
    public const string Tool = "keyload_search_wait_for_ann_index";
    /// <summary>Static discovery guidance without corpus or physical-owner disclosure.</summary>
    public const string Description = "Require one provisioned native ANN generation to cover an acknowledged minimum prefix under current authorization; never provision or rebuild it.";
    internal const string RequestAlias = "keyload.search.wait-for-ann-index-request.v1";
    internal const string ResultAlias = "keyload.search.wait-for-ann-index-result.v1";
}
