using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Uses the canonical persisted-authentication SDK transport for controlled partition movement.</summary>
public static class PartitionMovementClientExtensions
{
    private const string InvalidMove = "A nonempty stable movement identity and defined mode are required.";

    /// <summary>Transfers, resumes or aborts the exact bounded durable movement identity.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The unchanged logical scope and expected committed owner placement.</param>
    /// <param name="cancellationToken">Cancellation for the complete public parent request.</param>
    /// <returns>The normal typed durable movement phase or safe native failure.</returns>
    public static Task<Result<PartitionMoveResult>> MovePartitionAsync(this KeyLoadClient client,
        PartitionMoveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        if (request.MoveId == Guid.Empty || request.Mode == PartitionMoveMode.None || !Enum.IsDefined(request.Mode))
        { throw new ArgumentException(InvalidMove, nameof(request)); }
        return client.Send<PartitionMoveResult>(PartitionMovePublicProtocol.Route, request, true,
            request.MoveId, cancellationToken);
    }
}
