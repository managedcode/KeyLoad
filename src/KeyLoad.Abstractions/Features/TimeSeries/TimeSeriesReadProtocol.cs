namespace KeyLoad;

/// <summary>Defines the canonical additive HTTP and MCP time-series read identities.</summary>
public static class TimeSeriesReadProtocol
{
    /// <summary>HTTP route for the projected latest sample.</summary>
    public const string LatestRoute = "/v1/series/latest";
    /// <summary>HTTP route for complete raw half-open statistics.</summary>
    public const string AggregateRoute = "/v1/series/aggregate";
    /// <summary>HTTP route for dense bounded UTC windows.</summary>
    public const string WindowsRoute = "/v1/series/windows";
    /// <summary>Official MCP tool identity for the latest sample.</summary>
    public const string LatestTool = "keyload_series_latest";
    /// <summary>Official MCP tool identity for complete raw statistics.</summary>
    public const string AggregateTool = "keyload_series_aggregate";
    /// <summary>Official MCP tool identity for dense bounded windows.</summary>
    public const string WindowsTool = "keyload_series_windows";
}
