using KeyLoad.Core;
using ManagedCode.Communication;

namespace KeyLoad.Clustering;

internal sealed class GrainCommandExecutor(DatabaseEngine database, ICommitCoordinator coordinator, TimeProvider clock,
    GrainRequestCodec? codec = null, RequestWorkOwner? workOwner = null,
    IOnlineTextMaintenance? onlineText = null, Func<IDropTableExecution>? borrowDropTable = null)
{
    internal async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request, string actorKey,
        CancellationToken cancellationToken, IGrainContext? context = null)
    {
        var stage = GrainFailureStage.PartitionResolution;
        var coreEntered = false;
        try
        {
            ValidateRoute(request, actorKey, cancellationToken);
            stage = GrainFailureStage.CapabilityExecution;
            await using var work = (CapabilityWorkLifetime.Acquire(workOwner, request.Envelope.RequestId,
                RequestWorkKind.CommandCapability, cancellationToken)).Value;
            coreEntered = true;
            return await ExecuteCoreAsync(request, work, context,
                work?.Token ?? cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            var primaryError = coreEntered ? error : MarkFailure(error, stage);
            RequestWorkSettlement.Rethrow(primaryError);
            throw;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestWorkSettlement.Rethrow(error);
            throw;
        }
    }

    private async Task<GrainOperationReply> ExecuteCoreAsync(DecodedGrainRequest request,
        CapabilityWorkLifetime? work, IGrainContext? context, CancellationToken operationToken)
    {
        var stage = GrainFailureStage.CapabilityExecution;
        try
        {
            var budget = new ReadExecutionBudget(clock, operationToken);
            if (work is not null)
            {
                budget.AttachResourceLifetimeBinding(work);
            }
            var principal = await CommandCapabilityAuthorization.AuthorizeAsync(database, coordinator,
                clock, codec, request, context, operationToken).ConfigureAwait(true);
            var kind = request.Envelope.CommandKind
                ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            await CommandCapabilityAuthorization.ObserveAndValidateAsync(database,
                codec, request, GrainRequestPhase.BeforeSubmit, context, operationToken).ConfigureAwait(true);
            var original = (CommandWorkAdmission.Bind(database, request, principal, clock, work, budget)).Value;
            var result = await SubmitCommandAsync(kind, request, principal, budget, operationToken).ConfigureAwait(true);
            stage = GrainFailureStage.ReplyEncoding;
            await ObservePhaseAsync(request, GrainRequestPhase.SubmitReturned, context, operationToken).ConfigureAwait(true);
            var reply = GrainReplyFactory.Operation(result: result, cancellationToken: operationToken);
            if (work is null || reply.Error is not null)
            {
                return reply;
            }
            reply = reply with
            {
                WorkProof = CommandWorkAdmission.Capture(request, work, principal.PolicyEpoch)
            };
            await CommandResultWorkBinding.BindBeforePublicationAsync(database, coordinator, clock,
                request, reply, budget, work, operationToken).ConfigureAwait(true);
            if (original is not null && kind is OperationKind.Receive or OperationKind.ReceiveSubscription)
            {
                result = database.ResolveAppliedSubmissionOutcome(original, budget);
                var current = GrainReplyFactory.Operation(result: result, cancellationToken: operationToken);
                return current.Error is null ? current with { WorkProof = reply.WorkProof } : current;
            }
            return reply;
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            var primaryError = MarkFailure(error, stage);
            work?.PrimaryError = primaryError;
            RequestWorkSettlement.Rethrow(primaryError);
            throw;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            work?.PrimaryError = error;
            RequestWorkSettlement.Rethrow(error);
            throw;
        }
    }

    private async Task<Result<object>> SubmitCommandAsync(OperationKind kind, DecodedGrainRequest request,
        PrincipalRecord principal, ReadExecutionBudget budget, CancellationToken operationToken)
    {
        return kind switch
        {
            OperationKind.DropTable => await (borrowDropTable?.Invoke()
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest))
                .ExecuteAsync(request.Envelope.RequestId, request.Envelope.CommandId, principal.Id,
                    GrainPayload.Read<DropTableRequest>(request.Payload), operationToken).ConfigureAwait(true),
            OperationKind.Batch => await SubmitBatchAsync(request, principal, budget, operationToken).ConfigureAwait(true),
            OperationKind.CommitProjectionBatch => await SubmitCheckpointAsync(request, principal,
                operationToken).ConfigureAwait(true),
            OperationKind.OnlineTextPublicationPhase => await GrainOnlineTextPublicationCommand.SubmitAsync(
                database, coordinator, request, principal, onlineText, operationToken).ConfigureAwait(true),
            OperationKind.Bm25CorpusPublicationPhase => await GrainBm25CorpusPublicationCommand.SubmitAsync(
                database, coordinator, request, principal, operationToken).ConfigureAwait(true),
            _ => await coordinator.RuntimeSubmitAsync(kind, request.Envelope.CommandId, principal.Id,
                GrainPayload.ReadCommandInput(kind, request.Payload), sourcePlanningBudget: budget, cancellationToken: operationToken).ConfigureAwait(true)
        };
    }

    private Task<Result<object>> SubmitBatchAsync(DecodedGrainRequest request, PrincipalRecord principal,
        ReadExecutionBudget budget, CancellationToken token)
        => coordinator.RuntimeSubmitAsync(OperationKind.Batch, request.Envelope.CommandId,
                principal.Id, GrainPayload.ReadCommandInput(OperationKind.Batch, request.Payload), sourcePlanningBudget: budget, cancellationToken: token);

    private Task<Result<object>> SubmitCheckpointAsync(DecodedGrainRequest request, PrincipalRecord principal,
        CancellationToken cancellationToken)
        => (GrainOnlineTextCheckpointCommand.TrySubmit(database, coordinator, request, principal,
            onlineText, clock, cancellationToken)).Value
            ?? coordinator.RuntimeSubmitAsync(OperationKind.CommitProjectionBatch, request.Envelope.CommandId,
                principal.Id, GrainPayload.ReadCommandInput(OperationKind.CommitProjectionBatch, request.Payload), cancellationToken: cancellationToken);

    private static Exception MarkFailure(Exception error, GrainFailureStage stage)
    {
        if (GrainBoundaryErrors.Handles(error))
        {
            if (stage == GrainFailureStage.ReplyEncoding)
            {
                error.Data[GrainFailureDiagnostics.StageMetadataKey] = stage;
            }
            GrainFailureDiagnostics.Mark(error, stage);
        }
        return error;
    }

    private static void ValidateRoute(DecodedGrainRequest request, string actorKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
        if (!string.Equals(GrainPartitionResolver.Resolve(request), actorKey, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
    }

    private ValueTask ObservePhaseAsync(DecodedGrainRequest request, GrainRequestPhase phase,
        IGrainContext? context, CancellationToken cancellationToken)
    {
        var observerCodec = codec;
        return observerCodec is { HasPhaseObserver: true }
            ? observerCodec.ObservePhaseAsync(request, phase, context, cancellationToken)
            : ValueTask.CompletedTask;
    }
}
