using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Uses the shared authenticated SDK transport for atomic placement administration.</summary>
public static class AtomicPartitionPlacementClient
{
    private const string BindRoute = "/v1/admin/partition-placement/bind";
    private const string ReadRoute = "/v1/admin/partition-placement/read";
    private const string InvalidCommandId = "A nonempty stable command ID is required.";

    /// <summary>Submits one stable-ID administrator placement bind command.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="commandId">The caller-owned stable command identity.</param>
    /// <param name="request">The canonical bind request.</param>
    /// <param name="cancellationToken">Cancellation for the complete HTTP request.</param>
    /// <returns>The normal native command result.</returns>
    public static Task<Result<bool>> BindAtomicPartitionPlacementAsync(this KeyLoadClient client, Guid commandId,
        BindAtomicPartitionPlacementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        if (commandId == Guid.Empty)
        { throw new ArgumentException(InvalidCommandId, nameof(commandId)); }
        return client.Send<bool>(BindRoute, request, true, commandId, cancellationToken);
    }

    /// <summary>Reads one same-view placement witness under persisted administrator authorization.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The canonical read request.</param>
    /// <param name="cancellationToken">Cancellation for the complete HTTP request.</param>
    /// <returns>The generated same-view placement witness.</returns>
    public static Task<Result<AtomicPartitionPlacementResolution>> ReadAtomicPartitionPlacementAsync(
        this KeyLoadClient client, AtomicPartitionPlacementReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<AtomicPartitionPlacementResolution>(ReadRoute, request, false, null, cancellationToken);
    }
}
