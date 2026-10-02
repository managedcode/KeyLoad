namespace KeyLoad.Orleans;

/// <summary>Borrowed physical-node administration; callers first enforce current persisted administrator authority.</summary>
public interface INodeAdministration
{
    /// <summary>Create a durable backup of this physical node's canonical store.</summary>
    /// <param name="cancellationToken">Cancellation checked before starting the storage operation.</param>
    /// <returns>Backup identifier and materialized cut.</returns>
    Task<BackupReceipt> BackupAsync(CancellationToken cancellationToken);

    /// <summary>Read bounded command and HTTP admission diagnostics for this node.</summary>
    /// <returns>Configured limits and current counters.</returns>
    NodeAdmissionStatus Admission();

    /// <summary>Read this node's replica, materialization and routing status.</summary>
    /// <param name="cancellationToken">Cancellation of status acquisition.</param>
    /// <returns>The actual physical node's identity and status.</returns>
    Task<NodeStatus> StatusAsync(CancellationToken cancellationToken);
}
