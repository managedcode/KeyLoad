using KeyLoad.Client.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Controls dispatch with the canonical stable command identity and persisted server authority.</summary>
public static class DispatchClient
{
    private const string InvalidCommandId = "A nonempty stable command ID is required.";
    private const string PausedQueryValue = "true";
    private const string UnpausedQueryValue = "false";

    /// <summary>Pauses or resumes dispatch using the caller-owned stable command ID.</summary>
    /// <param name="client">The authenticated KeyLoad client.</param>
    /// <param name="commandId">The stable identity used for exact replay.</param>
    /// <param name="paused">Whether dispatch is paused.</param>
    /// <param name="cancellationToken">Cancellation for the complete HTTP operation.</param>
    /// <returns>The canonical committed boolean or a classified possibly-unknown write outcome.</returns>
    public static Task<Result<bool>> SetDispatchAsync(this KeyLoadClient client, Guid commandId, bool paused,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (commandId == Guid.Empty)
        { throw new ArgumentException(InvalidCommandId, nameof(commandId)); }
        var path = string.Concat(ClientApiRoutes.AdminDispatch, ClientApiRoutes.DispatchPausedQuery,
            paused ? PausedQueryValue : UnpausedQueryValue);
        return client.Send<bool>(path, null, true, commandId, cancellationToken, HttpMethod.Post);
    }
}
