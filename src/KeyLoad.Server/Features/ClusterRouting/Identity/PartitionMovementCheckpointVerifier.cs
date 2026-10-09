using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Preserves the ordered native checkpoint verifier interface over feature-owned proof responsibilities.</summary>
internal sealed class PartitionMovementCheckpointVerifier : IPartitionMovementCheckpointVerifier
{
    private readonly PartitionMovementReceiverIssuerVerifier issuer;
    private readonly PartitionMovementReceiverCheckpointProofs checkpoint;
    private readonly PartitionMovementReceiverEffectVerifier effect;
    private readonly PartitionMovementRetireCancellationVerifier retire;
    private readonly IOptions<NodeOptions> options;
    private readonly IOptions<ReplicaConfiguration> replicaConfiguration;
    private readonly IOptions<GrainRoutingOptions> routing;

    internal PartitionMovementCheckpointVerifier(IOptions<NodeOptions> options,
        IOptions<ReplicaConfiguration> replicaConfiguration, IOptions<GrainRoutingOptions> routing)
    {
        this.options = options;
        this.replicaConfiguration = replicaConfiguration;
        this.routing = routing;
        issuer = new(options, replicaConfiguration);
        checkpoint = new(options, replicaConfiguration, issuer);
        effect = new(options, replicaConfiguration, issuer, checkpoint);
        retire = new(options, replicaConfiguration);
    }

    public void RequireReceiverIssueCapacity(ReadOnlyMemory<byte> ownedNativeContext)
    {
        if (ownedNativeContext.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        var context = NativeSerialization.Deserialize<PartitionMoveReceiverIssueCapacityContext>(ownedNativeContext.Span);
        if (context.Version != PartitionMoveProtocol.Version || context.MaxBatchBytes <= PartitionMovementProtocol.NoResultBytes
            || context.MaxPhaseRecordsPerMove <= PartitionMovementProtocol.NoExaminedRecords || context.MaxRetainedMetadataBytesPerMove <= PartitionMovementProtocol.NoReadBytes)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof); }
        if (ownedNativeContext.Length > context.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        PartitionMovementParentCaptureLimits.RequireReceiverIssueCapacity(context, options.Value.ClusterId);
    }

    public string VerifyReceiverEffect(DatabaseEngine database, string principalId, Guid originalCommandId,
        ReadOnlyMemory<byte> ownedEnvelope, ReadExecutionBudget work)
        => effect.VerifyReceiverEffect(database, principalId, originalCommandId, ownedEnvelope, work);

    public string VerifyReceiverIssue(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work)
        => issuer.VerifyReceiverIssue(database, principalId, ownedBody, work);

    public void RequireReceiverAdministrator(DatabaseEngine database, string principalId, ReadExecutionBudget work)
        => PartitionMovementReceiverIssuerVerifier.RequireReceiverAdministrator(database, principalId, work);

    public string VerifyRetireCancellation(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work)
        => retire.VerifyRetireCancellation(database, principalId, ownedBody, work);

    public string VerifyRetireCancellationQuery(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work)
        => retire.VerifyRetireCancellationQuery(database, principalId, ownedBody, work);

    public ReadOnlyMemory<byte> VerifyTransferRead(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> originalReply, string signature, ReadExecutionBudget work)
        => PartitionMovementTransferProofVerifier.Verify(database, principalId, originalReply, signature, work,
            options.Value, replicaConfiguration.Value, routing.Value, PartitionMovementTransferDataAction.Open);

    public ReadOnlyMemory<byte> VerifyTransferCleanup(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> originalReply, string signature, ReadExecutionBudget work)
        => PartitionMovementTransferProofVerifier.Verify(database, principalId, originalReply, signature, work,
            options.Value, replicaConfiguration.Value, routing.Value, PartitionMovementTransferDataAction.Close);

    public string Verify(DatabaseEngine database, string principalId, ReadOnlyMemory<byte> ownedBody,
        ReadExecutionBudget work)
    {
        work.Check();
        if (ownedBody.IsEmpty || ownedBody.Length > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.InvalidProof); }
        var body = NativeSerialization.Deserialize<PartitionMoveCheckpointBody>(ownedBody.Span);
        if (body.Version != PartitionMoveProtocol.Version || body.OperatorPrincipalId != principalId)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var header = database.ReadPartitionMovementParentHeader(principalId, body.OriginalTransferRequest, work);
        if (header is not null && (header.Generation != body.ExpectedGeneration || header.TerminalResult is not null))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMovementProtocol.InvalidProof); }
        if (header is null && (body.Action != PartitionMoveCheckpointAction.Admit || body.ExpectedGeneration != PartitionMovementProtocol.InitialParentGeneration))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        retire.VerifyRetireCancellationCheckpoint(database, principalId, body, work);
        checkpoint.VerifyReceiverSourceCheckpoint(database, principalId, body, work);
        checkpoint.VerifyIssuerPacketCheckpoint(database, principalId, body, work);
        checkpoint.VerifyReceiverCheckpoint(database, principalId, body, work);
        if (body.OriginalCaptureWitness is { } witness)
        {
            if (header is null || body.Action != PartitionMoveCheckpointAction.Observe)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
            var original = database.ReadPartitionMovementParentPhase(principalId, body.OriginalTransferRequest,
                body.OriginalPhaseCommandId, work);
            PartitionMovementCaptureProofVerifier.Require(database, body, original, witness, work, options.Value);
        }
        else if (body.OriginalDescriptor is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        if (body.OriginalOutcomeWitness is { } outcome)
        {
            if (header is null || body.Action != PartitionMoveCheckpointAction.Observe || body.ObservedOriginalResult is null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
            var original = database.ReadPartitionMovementParentPhase(principalId, body.OriginalTransferRequest,
                body.OriginalPhaseCommandId, work);
            PartitionMovementOutcomeProofVerifier.Require(database, body, original, outcome, work, options.Value);
        }
        else if (body.ObservedOriginalResult is not null)
        {
            var original = database.ReadPartitionMovementParentPhase(principalId, body.OriginalTransferRequest,
                body.OriginalPhaseCommandId, work);
            if (!PartitionMoveGrantValidation.IsLocalControl(original.Stage))
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        }
        work.Check();
        return Convert.ToHexStringLower(SHA256.HashData(ownedBody.Span));
    }
}
