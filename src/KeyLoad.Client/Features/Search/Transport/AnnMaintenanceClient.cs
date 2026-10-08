using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Invokes administrator-only maintenance through the same authenticated native request stream.</summary>
public static class AnnMaintenanceClient
{
    /// <summary>Builds, restores or releases one exact node-owned disposable generation.</summary>
    /// <param name="client">The authenticated client; administrator authority is reloaded from canonical records.</param>
    /// <param name="request">Original stable maintenance identity and exact physical scope.</param>
    /// <param name="cancellationToken">Original bounded request cancellation.</param>
    /// <returns>Actual generation metadata and canonical child checkpoint evidence.</returns>
    public static Task<Result<AnnMaintenanceResult>> MaintainAnnIndexAsync(this KeyLoadClient client,
        AnnMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<AnnMaintenanceResult>(AnnMaintenanceProtocol.Route, request, true, request.CommandId, cancellationToken);
    }
}
