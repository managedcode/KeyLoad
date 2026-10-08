using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class ControlledDocumentPhaseExecution(PartitionMovementRuntime runtime,
    PhysicalShardRecord controlOwner, PartitionHost partition,
    Func<CancellationToken, ValueTask>? grantSettled = null,
    Func<CancellationToken, ValueTask>? outcomeObserved = null)
{
    private const int FirstReceiver = 0;
    internal bool HasAcknowledgedTargetEffect { get; private set; }

    internal async Task<PartitionMovePhaseResult> ApplyAsync<T>(Guid phaseCommandId,
        PartitionControlDocumentCommandContext context, PartitionMovePeerStage stage, T body,
        DateTimeOffset originalExpiry, PartitionMovePhaseGrant? grant,
        PartitionMoveJournalReceipt? authorization, ReadExecutionBudget originalWork,
        CancellationToken cancellationToken)
        => await ApplyEncodedAsync(phaseCommandId, context, stage, EncodeBody(body),
            originalExpiry, grant, authorization, originalWork, cancellationToken).ConfigureAwait(false);

    internal async Task<PartitionMovePhaseResult> ApplyEncodedAsync(Guid phaseCommandId,
        PartitionControlDocumentCommandContext context, PartitionMovePeerStage stage, ReadOnlyMemory<byte> body,
        DateTimeOffset originalExpiry, PartitionMovePhaseGrant? grant,
        PartitionMoveJournalReceipt? authorization, ReadExecutionBudget originalWork,
        CancellationToken cancellationToken)
    {
        RequireBody(body);
        originalWork.Check();
        var envelope = new PartitionMovePeerEnvelope(context.Control.Version, context.Control.MoveId,
            context.Control.Partition, controlOwner, context.Control.SourcePlacement,
            context.Control.DestinationOwner, PartitionMoveIntentIdentity.Digest(context.Control), stage,
            default, originalExpiry, Guid.NewGuid(), body, grant);
        var response = await runtime.DispatchProtectedDocumentAsync(phaseCommandId, envelope,
            authorization, originalWork, cancellationToken).ConfigureAwait(false);
        var reply = response.Reply;
        if (reply.Error is { } error)
        { throw Errors.Fail(error, reply.SafeDetail ?? RemoteDocumentProtocol.Unavailable); }
        var result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionMovePhaseResult
            ?? throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable);
        if (result.Journal.CommandId != phaseCommandId || result.MoveId != context.Control.MoveId
            || result.Stage != stage)
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable); }
        if (stage == PartitionMovePeerStage.ControlApplyCommand)
        { HasAcknowledgedTargetEffect = true; }
        originalWork.MeasureResult(result);
        originalWork.Check();
        return result;
    }
    internal PartitionMovePhaseCommand Propose<T>(PartitionControlDocumentCommandContext context,
        PartitionMovePeerStage stage, T body)
        => ProposeEncoded(context, stage, EncodeBody(body));

    internal PartitionMovePhaseCommand ProposeEncoded(PartitionControlDocumentCommandContext context,
        PartitionMovePeerStage stage, ReadOnlyMemory<byte> body)
    {
        RequireBody(body);
        return new(context.Control.Version, context.Control.MoveId, context.Control.Partition,
            controlOwner, context.Control.SourcePlacement, context.Control.DestinationOwner,
            PartitionMoveIntentIdentity.Digest(context.Control), stage, default,
            body, Resources: []);
    }

    internal async Task<PartitionMovePhaseResult> QueryOriginalAsync<T>(Guid phaseCommandId,
        PartitionControlDocumentCommandContext context, T body, PartitionMovePhaseGrant grant,
        PartitionMoveJournalReceipt authorization, DateTimeOffset requestExpiry,
        ReadExecutionBudget originalWork, CancellationToken cancellationToken)
        => await QueryOriginalEncodedAsync(phaseCommandId, context, EncodeBody(body),
            grant, authorization, requestExpiry, originalWork, cancellationToken).ConfigureAwait(false);

    internal async Task<PartitionMovePhaseResult> QueryOriginalEncodedAsync(Guid phaseCommandId,
        PartitionControlDocumentCommandContext context, ReadOnlyMemory<byte> body, PartitionMovePhaseGrant grant,
        PartitionMoveJournalReceipt authorization, DateTimeOffset requestExpiry,
        ReadExecutionBudget originalWork, CancellationToken cancellationToken)
    {
        RequireBody(body);
        originalWork.Check();
        var envelope = new PartitionMovePeerEnvelope(context.Control.Version, context.Control.MoveId,
            context.Control.Partition, controlOwner, context.Control.SourcePlacement,
            context.Control.DestinationOwner, PartitionMoveIntentIdentity.Digest(context.Control),
            grant.Stage, grant.PageOrdinal, grant.ExpiresAt, Guid.NewGuid(), body,
            grant with { Settlement = null, AbortDisposition = null });
        PartitionMovementOutcomeWitness witness;
        try
        {
            witness = await runtime.QueryOutcomeAsync(phaseCommandId, envelope, authorization,
                grant.ReceiverOwner.VoterIds[FirstReceiver], requestExpiry, originalWork, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyLoadException failure) when (failure.Code == ErrorCode.RecoveryRequired
            && failure.Message == ControlledDocumentFailureProtocol.MissingDurableOutcome)
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable);
            unknown.Data[ControlledDocumentFailureProtocol.OriginalOutcomeFailure] = failure;
            throw unknown;
        }
        if (witness.Result.Error is { } error)
        {
            var originalFailure = Errors.Fail(error, witness.Result.SafeDetail ?? RemoteDocumentProtocol.Unavailable);
            if (error != ErrorCode.RecoveryRequired
                || witness.Result.SafeDetail != ControlledDocumentFailureProtocol.MissingDurableOutcome)
            { throw originalFailure; }
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable);
            unknown.Data[ControlledDocumentFailureProtocol.OriginalOutcomeFailure] = originalFailure;
            throw unknown;
        }
        var result = witness.Result.Get<PartitionMovePhaseResult>();
        if (result.Journal.CommandId != phaseCommandId || result.MoveId != context.Control.MoveId
            || result.Stage != grant.Stage)
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable); }
        HasAcknowledgedTargetEffect = true;
        originalWork.Check();
        if (outcomeObserved is not null)
        { await outcomeObserved(cancellationToken).ConfigureAwait(false); }
        originalWork.Check();
        return result;
    }

    internal async Task<PartitionControlGrantSnapshot> CaptureGrantAsync(PartitionControlDocumentCommandContext context,
        PartitionMoveAuthorizeBody originalBody, DateTimeOffset originalExpiry, ReadExecutionBudget originalWork,
        CancellationToken cancellationToken)
    {
        await partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        var original = new PartitionMovePeerEnvelope(context.Control.Version, context.Control.MoveId,
            context.Control.Partition, controlOwner, context.Control.SourcePlacement,
            context.Control.DestinationOwner, PartitionMoveIntentIdentity.Digest(context.Control),
            PartitionMovePeerStage.ControlAuthorize, default, originalExpiry, Guid.NewGuid(),
            NativeSerialization.Serialize(originalBody));
        return partition.Database.CaptureControlledDocumentGrant(original, originalWork);
    }

    internal async ValueTask ObserveGrantSettledAsync(ReadExecutionBudget work, CancellationToken token)
    {
        work.Check();
        if (grantSettled is not null)
        { await grantSettled(token).ConfigureAwait(false); }
        work.Check();
    }

    private byte[] EncodeBody<T>(T body)
    {
        if (NativeSerialization.Measure(body) > partition.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        return NativeSerialization.Serialize(body);
    }

    private static void RequireBody(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.Unavailable); }
    }
}
