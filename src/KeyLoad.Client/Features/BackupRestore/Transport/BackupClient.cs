using KeyLoad.Client.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Creates an administrator-authorized physical backup through the shared signed transport.</summary>
public static class BackupClient
{
    /// <summary>Creates one physical backup; repeating this call can create another archive.</summary>
    /// <param name="client">The authenticated KeyLoad client.</param>
    /// <param name="cancellationToken">Cancellation for the complete HTTP operation.</param>
    /// <returns>The actual backup receipt or a classified possibly-unknown write outcome.</returns>
    public static Task<Result<BackupReceipt>> BackupAsync(this KeyLoadClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<BackupReceipt>(ClientApiRoutes.AdminBackup, null, true, null, cancellationToken,
            HttpMethod.Post);
    }
    /// <summary>Captures or replays the exact original immutable native archive on the expected physical node.</summary>
    /// <param name="client">The authenticated native SDK client.</param>
    /// <param name="request">Stable capture identity and independently observed exact owner/node tuple.</param>
    /// <param name="cancellationToken">Original complete HTTP cancellation.</param>
    /// <returns>The full original archive/cut receipt, or its actual classified failure.</returns>
    public static Task<Result<ClusterBackupOwnerReceipt>> CaptureClusterBackupOwnerAsync(this KeyLoadClient client,
        ClusterBackupOwnerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<ClusterBackupOwnerReceipt>(ClusterBackupProtocol.Route, request, true, null,
            cancellationToken, HttpMethod.Post);
    }
}
