namespace KeyLoad;

/// <summary>Stable shared SDK, MCP and SQL CALL operation identities.</summary>
public static class WaitForIndexProtocol
{
    /// <summary>The actual HTTP operation route.</summary>
    public const string Route = "/search/wait-for-index";
    /// <summary>The discovery-only canonical MCP and CALL operation name.</summary>
    public const string Tool = "keyload_search_wait_for_index";
    internal const string RequestAlias = "keyload.search.wait-for-index-request";
    internal const string ResultAlias = "keyload.search.wait-for-index-result";
}
