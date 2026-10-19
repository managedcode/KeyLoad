namespace KeyLoad;

/// <summary>Canonical authorized aggregate replay read transport identities.</summary>
public static class AggregateReplayProtocol
{
    /// <summary>HTTP route for the complete bounded replay slice.</summary>
    public const string Route = "/streams/replay";

    /// <summary>Official MCP tool for the same authorized read operation.</summary>
    public const string Tool = "keyload_streams_replay";
}
