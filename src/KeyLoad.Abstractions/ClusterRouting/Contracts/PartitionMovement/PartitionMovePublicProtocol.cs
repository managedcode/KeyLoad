namespace KeyLoad;

/// <summary>Names the one current public controlled-movement command and its safe interrupted outcome.</summary>
public static class PartitionMovePublicProtocol
{
    /// <summary>Canonical HTTP administration route shared by SDK and operation descriptors.</summary>
    public const string Route = "/v1/admin/partitions/move";
    /// <summary>Canonical on-demand MCP operation name.</summary>
    public const string ToolName = "keyload_admin_partition_move";
    /// <summary>Safe outcome when an admitted movement parent cannot return its durable terminal result.</summary>
    public const string Interrupted = "Partition movement was interrupted; resume or abort the same unpublished move identity.";
}
