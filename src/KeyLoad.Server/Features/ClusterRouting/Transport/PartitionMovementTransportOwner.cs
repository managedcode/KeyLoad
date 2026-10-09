using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns one original native transport resource context and purpose-specific authenticated channels.</summary>
internal sealed class PartitionMovementTransportOwner : IDisposable
{
    private readonly PartitionMovementTransportContext context;
    private readonly PartitionMovementEffectTransport effect;
    private readonly PartitionMovementReceiverIssueTransport issue;
    private readonly PartitionMovementTransferDataTransport transfer;

    internal PartitionMovementTransportOwner(IOptions<NodeOptions> options, PartitionHost partition,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock)
    {
        context = new(options, partition, membership, clock);
        var reply = new PartitionMovementTransportReplyVerifier(context);
        effect = new(context, reply);
        issue = new(context, reply);
        transfer = new(context);
    }

    public void Dispose() => context.Dispose();

    internal Task<PartitionMovementAuthenticatedReply> ExchangeOutcomeAsync(int index,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementOutcomeTransportRequest query,
        byte[] body, bool local, CancellationToken cancellationToken)
        => effect.ExchangeOutcomeAsync(index, receiver, query, body, local, cancellationToken);

    internal Task<PartitionMovementAuthenticatedReply> ExchangeReceiverIssueAsync(int index,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementReceiverExchange exchange,
        byte[] body, bool local, CancellationToken cancellationToken)
        => issue.ExchangeReceiverIssueAsync(index, receiver, exchange, body, local, cancellationToken);

    internal Task<PartitionMovementAuthenticatedReply> ExchangeRetireCancellationAsync(int index,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementReceiverExchange exchange,
        byte[] body, bool local, CancellationToken cancellationToken)
        => issue.ExchangeRetireCancellationAsync(index, receiver, exchange, body, local, cancellationToken);

    internal Task<PartitionMovementTransferDataReply> ExchangeTransferDataAsync(int index,
        RegisteredPhysicalOwnerV1 source, PartitionMovementTransferDataRequest original, byte[] body,
        bool local, CancellationToken cancellationToken)
        => transfer.ExchangeTransferDataAsync(index, source, original, body, local, cancellationToken);

    internal Task<PartitionMovementAuthenticatedReply> ExchangeAsync(int index, RegisteredPhysicalOwnerV1 receiver,
        PartitionMovementTransportRequest original, byte[] body, bool local, CancellationToken cancellationToken)
        => effect.ExchangeAsync(index, receiver, original, body, local, cancellationToken);
}
