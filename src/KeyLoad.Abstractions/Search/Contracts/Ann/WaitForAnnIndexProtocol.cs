namespace KeyLoad;

/// <summary>Stable readonly SDK, official MCP and shared SQL CALL identities.</summary>
public static class WaitForAnnIndexProtocol
{
    /// <summary>The authenticated operation route.</summary>
    public const string Route = "/search/ann/wait-for-index";
    /// <summary>The discovered MCP and Q1 CALL operation name.</summary>
    public const string Tool = "keyload_search_wait_for_ann_index";
    /// <summary>Static discovery guidance without corpus or physical-owner disclosure.</summary>
    public const string Description = "Require one provisioned execution ANN generation to cover an acknowledged minimum prefix under current authorization; never provision or rebuild it.";
    internal const string RequestAlias = "keyload.search.wait-for-ann-index-request";
    internal const string ResultAlias = "keyload.search.wait-for-ann-index-result";
}
