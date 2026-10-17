using KeyLoad.Client.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Calls the canonical native table retirement operation through the existing authenticated SDK transport.</summary>
public static class DropTableClientExtensions
{
    /// <summary>Drops one table after all required owner fences, owned work joins and canonical cleanup complete.</summary>
    /// <param name="client">The existing authenticated SDK client.</param>
    /// <param name="commandId">The stable original command ID, retained after an uncertain result.</param>
    /// <param name="request">The tenant, database and table name resolved by current persisted server authority.</param>
    /// <param name="cancellationToken">Cancels the caller without creating a replacement command.</param>
    /// <returns>The finalized canonical result, or the classified original failure.</returns>
    public static Task<Result<DropTableResult>> DropTableAsync(this KeyLoadClient client, Guid commandId,
        DropTableRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<DropTableResult>(ClientApiRoutes.TablesDrop, request, true, commandId, cancellationToken);
    }
}
