using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Composes the original native receiver API without retaining additional authority or storage ownership.</summary>
internal sealed class PartitionMovementReceiver
{
    private readonly PartitionMovementReceiverContext context;
    private readonly PartitionMovementReceiverReads reads;
    private readonly PartitionMovementReceiverIssuance issuance;
    private readonly PartitionMovementReceiverRetireCancellation retire;
    private readonly PartitionMovementReceiverCleanup cleanup;

    internal PartitionMovementReceiver(OrleansNode node, PartitionHost partition,
        IOptions<NodeOptions> options, TimeProvider clock, Func<PartitionMovementSourceOwner> source)
    {
        context = new(node, partition, options, clock, source);
        reads = new(context);
        issuance = new(context, reads);
        retire = new(context);
        cleanup = new(context);
    }

    internal Task<PartitionMoveReceiverIssuanceWitness> ReadReceiverIssuanceProofAsync(
        PartitionMovementReceiverIssueQuery requested, ReadExecutionBudget work, CancellationToken cancellationToken)
        => issuance.ReadReceiverIssuanceProofAsync(requested, work, cancellationToken);

    internal Task<GrainOperationReply> IssueReceiverAsync(PartitionMovementReceiverIssueRequest request,
        ReadOnlyMemory<byte> originalBytes, string originalSignature, ReadExecutionBudget work,
        CancellationToken cancellationToken)
        => issuance.IssueReceiverAsync(request, originalBytes, originalSignature, work, cancellationToken);

    internal Task<GrainOperationReply> IssueReceiverAsync(Guid commandId, PartitionMoveReceiverIssueBody body,
        ReadExecutionBudget work, CancellationToken cancellationToken)
        => issuance.IssueReceiverAsync(commandId, body, work, cancellationToken);

    internal Task<PartitionMoveReceiverIssuePacket> CreateReceiverIssuePacketAsync(string principalId,
        PartitionMoveRequest request, Guid phaseId, DateTimeOffset readExpiry, ReadExecutionBudget work,
        CancellationToken cancellationToken)
        => issuance.CreateReceiverIssuePacketAsync(principalId, request, phaseId, readExpiry, work, cancellationToken);

    internal Task<GrainOperationReply> ExecuteOutcomeAsync(PartitionMovementOutcomeTransportRequest query,
        CancellationToken cancellationToken)
        => context.ExecuteOutcomeAsync(query, cancellationToken);

    internal Task<GrainOperationReply> ApplyParentPhaseAsync(Guid commandId, PartitionMovePeerEnvelope envelope,
        CancellationToken cancellationToken)
        => context.ApplyAsync(commandId, envelope, cancellationToken);

    internal Task<OperationResult> ReadParentOriginalOutcomeAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentPhase original, DateTimeOffset expiry,
        ReadExecutionBudget work, CancellationToken cancellationToken)
        => reads.ReadParentOriginalOutcomeAsync(principalId, request, original, expiry, work, cancellationToken);

    internal Task<PartitionMoveParentState> ReadParentStateAsync(string principalId, PartitionMoveRequest request,
        Guid selectedPhaseId, DateTimeOffset expiry, ReadExecutionBudget work, CancellationToken cancellationToken)
        => reads.ReadParentStateAsync(principalId, request, selectedPhaseId, expiry, work, cancellationToken);

    internal Task<GrainOperationReply> CancelExpiredRetireAsync(PartitionMovementRetireCancellationRequest request,
        ReadOnlyMemory<byte> originalBytes, string originalSignature, ReadExecutionBudget work, CancellationToken cancellationToken)
        => retire.CancelExpiredRetireAsync(request, originalBytes, originalSignature, work, cancellationToken);

    internal Task<PartitionMovementTransportReply> ReadRetireCancellationAsync(PartitionMovementRetireCancellationQuery requested,
        ReadOnlyMemory<byte> originalBytes, string originalSignature, ReadExecutionBudget work, CancellationToken cancellationToken)
        => retire.ReadRetireCancellationAsync(requested, originalBytes, originalSignature, work, cancellationToken);

    internal string SignRetireCancellationReply(ReadOnlyMemory<byte> bytes)
        => retire.SignRetireCancellationReply(bytes);

    internal Task<PartitionMoveReceiverSourceWitness> ReadReceiverSourcePendingAsync(string principalId,
        PartitionMoveRequest request, Guid phaseId, DateTimeOffset expiry,
        ReadExecutionBudget work, CancellationToken cancellationToken)
        => reads.ReadReceiverSourcePendingAsync(principalId, request, phaseId, expiry, work, cancellationToken);

    internal Task<PartitionMovementAuthenticatedAuthority> ReadTransferAuthorityAsync(string principalId,
        PartitionMovementTransferAuthorityQuery query, DateTimeOffset expiry, ReadExecutionBudget work,
        CancellationToken cancellationToken)
        => reads.ReadTransferAuthorityAsync(principalId, query, expiry, work, cancellationToken);

    internal Task<GrainOperationReply> ExecuteTransferDataAsync(PartitionMovementTransferDataRequest request,
        CancellationToken cancellationToken)
        => reads.ExecuteTransferDataAsync(request, cancellationToken);

    internal ReplicaSiloDiscovery Discovery()
        => context.Discovery();

    internal PhysicalShardRecord LocalOwner()
        => context.LocalOwner();

    internal async Task<GrainOperationReply> ExecuteAsync(PartitionMovementTransportRequest verified,
        CancellationToken cancellationToken)
    {
        if (verified.Action == PartitionMovementTransportAction.Apply)
        { return await cleanup.ApplyPhaseAsync(verified.CommandId, verified.Envelope, cancellationToken).ConfigureAwait(false); }
        var action = verified.Action switch
        {
            PartitionMovementTransportAction.Capture => PartitionMovementCaptureAction.Capture,
            PartitionMovementTransportAction.Page => PartitionMovementCaptureAction.Page,
            PartitionMovementTransportAction.Release => PartitionMovementCaptureAction.Release,
            _ => throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMoveProtocol.Invalid)
        };
        var principal = context.Administrator(cancellationToken);
        var requestId = Guid.NewGuid();
        var capability = new PartitionMovementCaptureCapability(action, verified.Envelope,
            verified.HandleId, verified.Ordinal);
        var signed = context.Node.CatalogRequestCodec().CreatePartitionMovementCapture(requestId, principal.Id, capability);
        return await context.ExecuteSignedAsync(principal, requestId, Guid.Empty, signed, command: false,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<PartitionMovePhaseResult> SettleCaptureAsync(PartitionMovePeerEnvelope original,
        CancellationToken cancellationToken)
    {
        var commandId = original.Grant?.PhaseCommandId
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.Invalid);
        var reply = await context.ApplyAsync(commandId, original, cancellationToken).ConfigureAwait(false);
        if (reply.Error is { } code)
        { throw Errors.Fail(code, reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var value = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionMovePhaseResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
        return value;
    }
}
