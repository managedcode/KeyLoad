namespace KeyLoad.Clustering;

public partial interface IDatabaseCommitGrain
{
    /// <summary>Awaits the original local request at the one open shared-file owner.</summary>
    Task<GrainOperationReply> ExecuteSharedAsync(string signedRequest, string? actorKey, CancellationToken cancellationToken);

    /// <summary>Cancels and joins the original shared-file maintenance session.</summary>
    Task AbortSharedMaintenanceAsync(GrainReadKind kind, Guid sessionId);

    /// <summary>Reads the actual shared-file owner's status.</summary>
    Task<NodeStatus> ReadSharedStatusAsync(CancellationToken cancellationToken);

    /// <summary>Reads the actual shared-file owner's admission pools.</summary>
    Task<NodeAdmissionStatus> ReadSharedAdmissionAsync(CancellationToken cancellationToken);

    /// <summary>Reads the actual shared files and their physical owner.</summary>
    Task<AdminNodeSnapshot> ReadSharedDashboardAsync(CancellationToken cancellationToken);

    /// <summary>Creates a backup within the shared physical lifetime.</summary>
    Task<BackupReceipt> CreateSharedBackupAsync(CancellationToken cancellationToken);
}
