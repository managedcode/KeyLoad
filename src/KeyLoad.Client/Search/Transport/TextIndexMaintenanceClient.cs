using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Invokes administrator-only maintenance through the same authenticated execution request stream.</summary>
public static class TextIndexMaintenanceClient
{
    /// <summary>Builds, restores or releases one exact node-owned disposable generation.</summary>
    /// <param name="client">The authenticated client; administrator authority is reloaded from canonical records.</param>
    /// <param name="request">Original stable maintenance identity and exact physical scope.</param>
    /// <param name="cancellationToken">Original bounded request cancellation.</param>
    /// <returns>Actual generation metadata and canonical child checkpoint evidence.</returns>
    public static Task<Result<TextIndexMaintenanceResult>> MaintainTextIndexAsync(this KeyLoadClient client,
        TextIndexMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request TextIndexMaintenanceRequest reference through its object parameter. */;
        return client.Send<TextIndexMaintenanceResult, TextIndexMaintenanceRequest>(TextIndexMaintenanceProtocol.Route, request, true, request.CommandId, cancellationToken);
    }
}
