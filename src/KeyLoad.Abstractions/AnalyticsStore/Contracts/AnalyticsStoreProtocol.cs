namespace KeyLoad;

/// <summary>Canonical public operation identities for independently named user analytics stores.</summary>
public static class AnalyticsStoreProtocol
{
    /// <summary>Stable batch upsert mutation discriminator.</summary>
    public const string UpsertKind = "upsertAnalyticsEvents";
    /// <summary>Stable batch delete mutation discriminator.</summary>
    public const string DeleteKind = "deleteAnalyticsEvents";
    /// <summary>HTTP route for authorized raw event pages.</summary>
    public const string ReadRoute = "/v1/analytics/read";
    /// <summary>HTTP route for complete native aggregates.</summary>
    public const string AggregateRoute = "/v1/analytics/aggregate";
    /// <summary>Official MCP and T-SQL EXEC raw-read identity.</summary>
    public const string ReadTool = "keyload_analytics_read";
    /// <summary>Official MCP and T-SQL EXEC aggregate identity.</summary>
    public const string AggregateTool = "keyload_analytics_aggregate";
}
