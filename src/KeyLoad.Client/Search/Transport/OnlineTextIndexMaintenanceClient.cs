using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Client operations for online publication of an existing administrator-configured text consumer.</summary>
public static class OnlineTextIndexMaintenanceClient
{
    /// <summary>Stages, catches up and canonically publishes one bounded online text generation.</summary>
    /// <param name="client">Authenticated client; persisted authority is reloaded for the operation.</param>
    /// <param name="request">Original stable command identity and exact configured consumer and physical scope.</param>
    /// <param name="cancellationToken">Original request cancellation; its deadline is never renewed.</param>
    /// <returns>The complete original result with actual canonical publication and checkpoint evidence.</returns>
    public static Task<Result<OnlineTextIndexMaintenanceResult>> MaintainOnlineTextIndexAsync(this KeyLoadClient client,
        OnlineTextIndexMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<OnlineTextIndexMaintenanceResult>(OnlineTextIndexMaintenanceProtocol.Route,
            request, true, request.CommandId, cancellationToken);
    }
}
