using System.Collections.Immutable;
using ManagedCode.Communication;

namespace KeyLoad.Client;

internal static class QueueWorkerObservations
{
    internal static QueueWorkerClaimIdentity Identity(Delivery delivery)
        => new(delivery.Id, delivery.Token, delivery.LeaseVersion, delivery.LeaseUntil,
            delivery.Attempt, delivery.DeliveryGeneration);

    internal static QueueWorkerOperationObservation Receive(ReceiveRequest request, Result<ReceiveResult> result)
        => new(request, null, result.Value?.Token,
            result.Value?.Deliveries.Select(Identity).ToImmutableArray() ?? [], null, result.Problem, result.Value?.RequestId);

    internal static QueueWorkerOperationObservation Completion(DeliveryCommand command, Result<CommitReceipt> result)
        => new(null, command, null, [], result, null);

    internal static bool Unknown<T>(Result<T> result)
        => result.IsFailed && result.Problem?.ErrorCode == QueueWorkerProtocol.Unknown;
}
