using KeyLoad.Core;

namespace KeyLoad.Orleans;

/// <summary>Borrowed physical-node administration; callers first enforce current persisted administrator authority.</summary>
public interface INodeAdministration
{
    /// <summary>Observes actual node files and process counters without acquiring storage ownership.</summary>
    /// <param name="cancellationToken">Cancellation of bounded observation work.</param>
    /// <returns>The actual executing physical node's administrator snapshot.</returns>
    Task<AdminNodeSnapshot> DashboardAsync(CancellationToken cancellationToken);

    /// <summary>Create a durable backup of this physical node's canonical store.</summary>
    /// <param name="cancellationToken">Cancellation checked before starting the storage operation.</param>
    /// <returns>Backup identifier and materialized cut.</returns>
    Task<BackupReceipt> BackupAsync(CancellationToken cancellationToken);

    /// <summary>Captures or verifies one stable native owner archive under current persisted credential authority.</summary>
    /// <param name="principalId">Actual freshly authenticated request principal.</param>
    /// <param name="capability">Server-created native request and credential witness sealed by the unique read grain.</param>
    /// <param name="work">Original bounded request lifetime and work.</param>
    /// <param name="cancellationToken">Original cancellation, retained by the joined native producer.</param>
    /// <returns>The exact complete original owner archive receipt.</returns>
    Task<ClusterBackupOwnerReceipt> CaptureClusterBackupOwnerAsync(string principalId,
        ReadOnlyMemory<byte> capability, ReadExecutionBudget work, CancellationToken cancellationToken);

    /// <summary>Read bounded command and HTTP admission diagnostics for this node.</summary>
    /// <returns>Configured limits and current counters.</returns>
    NodeAdmissionStatus Admission();

    /// <summary>Read this node's replica, materialization and routing status.</summary>
    /// <param name="cancellationToken">Cancellation of status acquisition.</param>
    /// <returns>The actual physical node's identity and status.</returns>
    Task<NodeStatus> StatusAsync(CancellationToken cancellationToken);
}
