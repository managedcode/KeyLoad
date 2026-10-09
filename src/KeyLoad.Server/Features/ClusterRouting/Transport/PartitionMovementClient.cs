using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Coordinates one original phase through its owned configured transport without retries.</summary>
internal sealed class PartitionMovementClient : IPartitionMovementDispatcher, IDisposable
{
    internal const int FirstVoter = 0;
    internal const int MinimumBodyBytes = 1;
    internal readonly IOptions<NodeOptions> options;
    internal readonly OrleansNode node;
    internal readonly PartitionHost partition;
    internal readonly TimeProvider clock;
    internal readonly PartitionMovementTransportOwner transport;

    internal PartitionMovementClient(IOptions<NodeOptions> options, OrleansNode node, PartitionHost partition,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock)
    {
        this.options = options;
        this.node = node;
        this.partition = partition;
        this.clock = clock;
        transport = new(options, partition, membership, clock);
    }

    public void Dispose() => transport.Dispose();
    public Task<PartitionMovementDispatchResult> DispatchAsync(Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization,
        PartitionMovementPeerAction action, Guid handleId, int ordinal, string? pinnedVoter,
        CancellationToken cancellationToken)
        => PartitionMovementClientDispatchOperations.DispatchAsync(this, phaseCommandId, original, authorization, action, handleId, ordinal, pinnedVoter, cancellationToken);

    internal Task<PartitionMovementDispatchResult> DispatchParentOriginalAsync(PartitionMoveParentPhase original,
        PartitionMoveReceiverSourceWitness dispatch, ReadExecutionBudget work, CancellationToken cancellationToken)
        => PartitionMovementClientDispatchOperations.DispatchParentOriginalAsync(this, original, dispatch, work, cancellationToken);

    internal Task<PartitionMovementDispatchResult> CaptureParentOriginalAsync(PartitionMoveParentPhase original,
        PartitionMoveReceiverSourceWitness dispatch, ReadExecutionBudget work, CancellationToken cancellationToken)
        => PartitionMovementClientDispatchOperations.CaptureParentOriginalAsync(this, original, dispatch, work, cancellationToken);

    internal Task<PartitionMovementDispatchResult> ReleaseParentCaptureAsync(PartitionMoveParentPhase original,
        Guid handleId, string originalVoter, PartitionMoveReceiverSourceWitness dispatch,
        ReadExecutionBudget work, CancellationToken cancellationToken)
        => PartitionMovementClientDispatchOperations.ReleaseParentCaptureAsync(this, original, handleId, originalVoter, dispatch, work, cancellationToken);

    public Task<PartitionMovementOutcomeWitness> QueryOutcomeAsync(Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization, string originalVoter,
        DateTimeOffset requestExpiry, ReadExecutionBudget work, CancellationToken cancellationToken)
        => PartitionMovementClientOutcomeOperations.QueryOutcomeAsync(this, phaseCommandId, original, authorization, originalVoter, requestExpiry, work, cancellationToken);

    internal Task<PartitionMovementDispatchResult> DispatchProtectedDocumentAsync(Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization,
        ReadExecutionBudget work, CancellationToken cancellationToken)
        => PartitionMovementClientProtectedDocumentOperations.DispatchProtectedDocumentAsync(this, phaseCommandId, original, authorization, work, cancellationToken);

    internal Task IssueReceiverFirstAsync(PartitionMoveParentPhase original,
        ReadExecutionBudget work, CancellationToken cancellationToken)
        => PartitionMovementClientReceiverIssueOperations.IssueReceiverFirstAsync(this, original, work, cancellationToken);

    internal Task<PartitionMoveReceiverIssuanceWitness> QueryReceiverIssueAsync(PartitionMoveParentPhase original,
        DateTimeOffset expiry, ReadExecutionBudget work, CancellationToken cancellationToken)
        => PartitionMovementClientReceiverIssueOperations.QueryReceiverIssueAsync(this, original, expiry, work, cancellationToken);

    internal PartitionMoveRetireCancellationAttempt CreateRetireCancellationAttempt(PartitionMoveParentPhase original,
        Guid cancellationId, PartitionMoveReceiverSourceWitness freshSource, DateTimeOffset expiry, ReadExecutionBudget work)
        => PartitionMovementClientRetireCancellationOperations.CreateRetireCancellationAttempt(this, original, cancellationId, freshSource, expiry, work);

    internal Task SendRetireCancellationFirstAsync(PartitionMoveParentPhase original,
        PartitionMoveRetireCancellationAttempt first, ReadExecutionBudget work, CancellationToken cancellationToken)
        => PartitionMovementClientRetireCancellationOperations.SendRetireCancellationFirstAsync(this, original, first, work, cancellationToken);

    internal Task<PartitionMovementAuthenticatedReply> QueryRetireCancellationAsync(PartitionMoveParentPhase original,
        Guid cancellationId, DateTimeOffset expiry, ReadExecutionBudget work, CancellationToken cancellationToken)
        => PartitionMovementClientRetireCancellationOperations.QueryRetireCancellationAsync(this, original, cancellationId, expiry, work, cancellationToken);

    internal static PartitionMovePeerEnvelope RetireCancellationOriginalEnvelope(PartitionMoveParentPhase original)
        => PartitionMovementClientRetireCancellationOperations.RetireCancellationOriginalEnvelope(original);

    internal Task<GrainOperationReply> ReadTransferDataAsync(string principalId,
        PartitionMovementAuthenticatedAuthority originalAuthority, PartitionMovementTransferDataAction action,
        Guid handleId, int ordinal, DateTimeOffset expiry, ReadExecutionBudget work, CancellationToken cancellationToken)
        => PartitionMovementClientTransferReadOperations.ReadTransferDataAsync(this, principalId, originalAuthority, action, handleId, ordinal, expiry, work, cancellationToken);
}
