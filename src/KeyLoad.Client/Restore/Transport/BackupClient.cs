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
}
