namespace KeyLoad;

/// <summary>Literal public identity for the bounded complete canonical distributed search profile.</summary>
public static class DistributedSearchProtocol
{
    /// <summary>Actual native gateway operation name.</summary>
    public const string Tool = "keyload_query_distributed_search";
    /// <summary>Actual authenticated HTTP operation route.</summary>
    public const string Route = "/v1/query/distributed-search";
    /// <summary>Public effect, bounded profile and scoped-result contract.</summary>
    public const string Description = "Read complete globally ranked canonical text, vector or hybrid results across explicit authorized owners. Every phase validates its original scoped cut and current persisted policy. Results have separate partition witnesses and an opaque statistics epoch; there is no global snapshot or cursor. Exact work that exceeds existing bounds fails without a partial page; explicit selected index generations use their existing separate APIs.";
}
