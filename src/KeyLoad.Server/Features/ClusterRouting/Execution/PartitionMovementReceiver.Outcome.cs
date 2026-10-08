using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementReceiver
{
    internal async Task<GrainOperationReply> ExecuteOutcomeAsync(PartitionMovementOutcomeTransportRequest query,
        CancellationToken cancellationToken)
    {
        var principal = Administrator(PartitionMovementControlPrincipal.Resolve(partition.Database,
            query.Original.Envelope), cancellationToken);
        var requestId = Guid.NewGuid();
        var capability = new PartitionMovementOutcomeQuery(query.Original.CommandId, query.Original.Envelope,
            query.MaximumReadBytes, query.MaximumExaminedRecords, query.MaximumResultBytes);
        var signed = node.CatalogRequestCodec().CreatePartitionMovementOutcome(requestId, principal.Id,
            capability, query.ExpiresAt);
        return await ExecuteSignedAsync(principal, requestId, Guid.Empty, signed, command: false,
            cancellationToken).ConfigureAwait(false);
    }
}
