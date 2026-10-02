namespace KeyLoad;

/// <summary>Stable additive administration routes, tools and bounded page defaults.</summary>
public static class AdminDashboardProtocol
{
    /// <summary>Physical node observation route.</summary>
    public const string SnapshotPath = "/v1/admin/dashboard";
    /// <summary>Database resource metadata page route.</summary>
    public const string ResourcesPath = "/v1/admin/dashboard/resources";
    /// <summary>Non-consuming queue metadata page route.</summary>
    public const string QueuePath = "/v1/admin/dashboard/queue";
    /// <summary>Official MCP node observation tool.</summary>
    public const string SnapshotTool = "keyload_admin_dashboard";
    /// <summary>Official MCP catalog page tool.</summary>
    public const string ResourcesTool = "keyload_admin_resources_list";
    /// <summary>Official MCP non-consuming queue page tool.</summary>
    public const string QueueTool = "keyload_admin_queue_browse";
    /// <summary>Default bounded metadata page size.</summary>
    public const int DefaultPageSize = 50;
    /// <summary>Maximum bounded metadata page size.</summary>
    public const int MaximumPageSize = 100;
}
