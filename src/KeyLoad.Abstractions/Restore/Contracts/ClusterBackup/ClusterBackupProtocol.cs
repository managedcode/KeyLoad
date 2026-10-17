namespace KeyLoad;

/// <summary>Frozen native owner capture wire names, independent of its persisted authorization.</summary>
public static class ClusterBackupProtocol
{
    /// <summary>The typed authenticated public owner capture endpoint.</summary>
    public const string Route = "/v1/admin/cluster-backup";
    /// <summary>The canonical official MCP and Q1 operation name.</summary>
    public const string Tool = "keyload_admin_cluster_backup";
    /// <summary>Bounded static catalog guidance without credentials or user data.</summary>
    public const string Description = "Capture or replay one complete native owner archive under fresh persisted administrator authority. Reuse the identical capture ID, physical tuple and native node ID for exact original archive replay; a complete cluster backup requires every registered or assigned owner archive.";
}
