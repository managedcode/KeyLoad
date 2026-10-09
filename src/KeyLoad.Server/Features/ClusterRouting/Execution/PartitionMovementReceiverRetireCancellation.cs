using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns retire responsibility while borrowing the single native receiver owner and original operation tokens.</summary>
internal sealed class PartitionMovementReceiverRetireCancellation(PartitionMovementReceiverContext context)
{
    internal async Task<GrainOperationReply> CancelExpiredRetireAsync(PartitionMovementRetireCancellationRequest request,
        ReadOnlyMemory<byte> originalBytes, string originalSignature, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        var principal = context.Administrator(cancellationToken);
        var body = new PartitionMoveRetireCancellationBody(request.Version, request.CancellationCommandId,
            request.OriginalPhaseCommandId, request.OriginalEnvelope, request.OriginalAuthorization,
            request.CleanupGeneration, originalBytes, originalSignature, request.CancellationExpiresAt,
            OriginalSourceWitness: request.SourceWitness);
        var operation = context.Partition.Database.CreateVerifiedPartitionMovementRetireCancellation(principal.Id, body, work);
        var requestId = Guid.NewGuid();
        var signed = context.Node.CatalogRequestCodec().CreatePartitionMovementCommand(requestId, operation, request.CancellationExpiresAt);
        var actual = await context.ExecuteSignedAsync(principal, requestId, request.CancellationCommandId, signed,
            command: true, cancellationToken).ConfigureAwait(false);
        work.Check();
        return actual;
    }

    internal async Task<PartitionMovementTransportReply> ReadRetireCancellationAsync(PartitionMovementRetireCancellationQuery requested,
        ReadOnlyMemory<byte> originalBytes, string originalSignature, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        var principal = context.Administrator(cancellationToken);
        await context.Partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        var grant = work.CreateReadGrant(requested.MaximumReadBytes, requested.MaximumExaminedRecords);
        var body = new PartitionMoveRetireCancellationReadBody(requested.Version, requested.OriginalPhaseCommandId,
            requested.CancellationCommandId, requested.OriginalEnvelope, requested.OriginalAuthorization,
            originalBytes, originalSignature, requested.QueryExpiresAt);
        var query = new PartitionMovementRetireCancellationOutcomeQuery(body, grant.RemainingBytes,
            grant.RemainingRecords, Math.Min(requested.MaximumResultBytes, work.MaximumResultBytes));
        var requestId = Guid.NewGuid();
        var signed = context.Node.CatalogRequestCodec().CreatePartitionMovementRetireCancellationOutcome(requestId, principal.Id,
            query, requested.QueryExpiresAt);
        var terminal = await context.ExecuteSignedAsync(principal, requestId, Guid.Empty, signed, command: false,
            cancellationToken).ConfigureAwait(false);
        if (terminal.Error is { } code)
        { throw Errors.Fail(code, terminal.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var actual = GrainNativePayload.Read<GrainValue>(terminal.Payload).Value as PartitionMovementRetireCancellationOutcomeResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        work.ImportReadGrant(grant, actual.ReadBytes, actual.ExaminedRecords);
        work.CompleteReadGrant(grant);
        work.MeasureResult(actual);
        work.Check();
        return new(requested.CancellationCommandId, requested.QueryNonce, context.LocalOwner(), context.Discovery(), terminal,
            PartitionMoveOriginalDispatchIdentity.Digest(requested.OriginalPhaseCommandId, requested.OriginalEnvelope));
    }

    internal string SignRetireCancellationReply(ReadOnlyMemory<byte> bytes)
    {
        var key = Convert.FromBase64String(context.Options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            return mac.SignRetireCancellationReply(bytes.Span);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
