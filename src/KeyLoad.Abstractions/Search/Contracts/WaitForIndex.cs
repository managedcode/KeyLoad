namespace KeyLoad;

/// <summary>Requires native lexical publication at an acknowledged minimum applied cut.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(WaitForIndexProtocol.RequestAlias)]
public sealed record WaitForIndexRequest(
    [property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Collection,
    [property: Orleans.Id(2)] string TextField,
    [property: Orleans.Id(3)] CommitToken MinimumToken);

/// <summary>Reports the applied cut whose eligible native lexical generation completed publication.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(WaitForIndexProtocol.ResultAlias)]
public sealed record WaitForIndexResult(
    [property: Orleans.Id(0)] CommitToken AppliedToken,
    [property: Orleans.Id(1)] long SchemaVersion,
    [property: Orleans.Id(2)] long PolicyEpoch);

/// <summary>Stable shared SDK, MCP and SQL CALL operation identities.</summary>
public static class WaitForIndexProtocol
{
    /// <summary>The actual HTTP operation route.</summary>
    public const string Route = "/v1/search/wait-for-index";
    /// <summary>The discovery-only canonical MCP and CALL operation name.</summary>
    public const string Tool = "keyload_search_wait_for_index";
    internal const string RequestAlias = "keyload.search.wait-for-index-request.v1";
    internal const string ResultAlias = "keyload.search.wait-for-index-result.v1";
}
