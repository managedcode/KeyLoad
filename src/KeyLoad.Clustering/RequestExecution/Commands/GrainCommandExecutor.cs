using KeyLoad.Core;
using KeyLoad.Core.BlobStorage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

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
                work?.Token ?? cancellationToken);
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
            var budget = new ReadExecutionBudget(operationToken);
            if (work is not null)
            {
                budget.AttachResourceLifetimeBinding(work);
            }
            var principal = await CommandCapabilityAuthorization.AuthorizeAsync(database,
                clock, codec, request, context, operationToken);
            var kind = request.Envelope.CommandKind
                ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            await CommandCapabilityAuthorization.ObserveAndValidateAsync(database,
                codec, request, GrainRequestPhase.BeforeSubmit, context, operationToken);
            var original = (CommandWorkAdmission.Bind(database, request, principal, clock, work, budget)).Value;
            var reply = await SubmitCommandAsync(kind, request, principal, original, budget, context, operationToken);
            stage = GrainFailureStage.ReplyEncoding;
            if (work is null || reply.Error is not null)
            {
                return reply;
            }
            reply = reply with
            {
                WorkProof = CommandWorkAdmission.Capture(request, work, principal.PolicyEpoch)
            };
            await CommandResultWorkBinding.BindBeforePublicationAsync(database,
                request, reply, budget, work, operationToken);
            if (original is not null && kind is OperationKind.Receive or OperationKind.ReceiveSubscription)
            {
                var /* KLD0041: Read the already-persisted receive submission outcome in its current mixed-result representation before encoding the public reply. */ result = (database.ResolveAppliedSubmissionOutcome(original, budget) /* KLD0041: Read the already-persisted receive submission outcome in its current mixed-result representation before encoding the public reply. */);
                var current = (GrainReplyFactory.Operation(result: result, cancellationToken: operationToken) /* KLD0041: Read the already-persisted receive submission outcome in its current mixed-result representation before encoding the public reply. */);
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

    private Task<GrainOperationReply> SubmitCommandAsync(OperationKind kind, DecodedGrainRequest request,
        PrincipalRecord principal, Command<DatabaseCommand>? original, ReadExecutionBudget budget,
        IGrainContext? context, CancellationToken token)
    {
        if (kind == OperationKind.DropTable)
        {
            return CompleteAsync((borrowDropTable?.Invoke()
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest))
                .ExecuteAsync(request.Envelope.RequestId, request.Envelope.CommandId, principal.Id,
                    (GrainPayload.Read<DropTableRequest>((request.Payload /* KLD0041: Decode the selected DropTable envelope slot as its current typed request. */)) /* KLD0041: Validate the exact DropTable request before the owned drop lifecycle. */), token), request, context, token);
        }
        var admitted = original ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        return kind switch
        {
            OperationKind.CommitProjectionBatch => CompleteAsync(SubmitCheckpointAsync(request, principal, admitted, token), request, context, token),
            OperationKind.OnlineTextPublicationPhase => CompleteAsync(GrainOnlineTextPublicationCommand.SubmitAsync(
                database, coordinator, request, principal, onlineText, token), request, context, token),
            OperationKind.Bm25CorpusPublicationPhase => CompleteAsync(GrainBm25CorpusPublicationCommand.SubmitAsync(
                database, coordinator, request, principal, token), request, context, token),
            OperationKind.Batch or OperationKind.Delivery or OperationKind.Processing or OperationKind.SubscriptionDelivery
                => SubmitAsync<CommitReceipt>(admitted, request, budget, context, token),
            OperationKind.Receive => SubmitAsync<ReceiveResult>(admitted, request, budget, context, token),
            OperationKind.CommitInbox => SubmitAsync<CommitInboxResult>(admitted, request, budget, context, token),
            OperationKind.ConfigureResource or OperationKind.CreateTable or OperationKind.SetQueueDispatch
                => SubmitAsync<ResourceDefinition>(admitted, request, budget, context, token),
            OperationKind.ConfigurePrincipal => SubmitAsync<PrincipalRecord>(admitted, request, budget, context, token),
            OperationKind.ConfigureApiKey or OperationKind.SetDispatch => SubmitAsync<bool>(admitted, request, budget, context, token),
            OperationKind.RuntimeJournal => SubmitAsync<RuntimeJournalMutationResult>(admitted, request, budget, context, token),
            OperationKind.FileMetadata => SubmitFileMetadataAsync(request, admitted, budget, context, token),
            OperationKind.ConfigureSubscription or OperationKind.SeekSubscription or OperationKind.SetSubscriptionPaused
                => SubmitAsync<SubscriptionInfo>(admitted, request, budget, context, token),
            OperationKind.ReceiveSubscription => SubmitAsync<ReceiveSubscriptionResult>(admitted, request, budget, context, token),
            OperationKind.SubscriptionProcessing => SubmitAsync<SubscriptionProcessingResult>(admitted, request, budget, context, token),
            OperationKind.ConfigureProjectionConsumer or OperationKind.ReleaseProjectionConsumer
                => SubmitAsync<ProjectionConsumerInfo>(admitted, request, budget, context, token),
            OperationKind.PurgeOutbox => SubmitAsync<OutboxHead>(admitted, request, budget, context, token),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private Task<GrainOperationReply> SubmitAsync<TResult>(Command<DatabaseCommand> admitted,
        DecodedGrainRequest request, ReadExecutionBudget budget, IGrainContext? context, CancellationToken token)
        => CompleteAsync(coordinator.SubmitVerifiedAsync<TResult>(admitted, token, budget), request, context, token);

    private Task<GrainOperationReply> SubmitFileMetadataAsync(DecodedGrainRequest request,
        Command<DatabaseCommand> admitted, ReadExecutionBudget budget, IGrainContext? context, CancellationToken token)
    {
        var mutation = (GrainPayload.ReadCommand<FileMetadataMutation>((request.Payload /* KLD0041: Read the current native envelope slot only to validate the file metadata action before selecting its concrete reply type. */)) /* KLD0041: Require the exact file metadata mutation from the current envelope before selecting FileUpload, FileEntry or bool completion. */);
        return mutation.Action switch
        {
            FileMetadataAction.Reserve or FileMetadataAction.Stage or FileMetadataAction.Abort
                => SubmitAsync<FileUpload>(admitted, request, budget, context, token),
            FileMetadataAction.Publish or FileMetadataAction.Delete
                => SubmitAsync<FileEntry>(admitted, request, budget, context, token),
            FileMetadataAction.Reclaim => SubmitAsync<bool>(admitted, request, budget, context, token),
            _ => throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid)
        };
    }

    private async Task<GrainOperationReply> CompleteAsync<TResult>(Task<Result<TResult>> submission,
        DecodedGrainRequest request, IGrainContext? context, CancellationToken token)
    {
        var result = await submission;
        try
        {
            await ObservePhaseAsync(request, GrainRequestPhase.SubmitReturned, context, token);
            return GrainReplyFactory.Operation(result, token);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            throw MarkFailure(error, GrainFailureStage.ReplyEncoding);
        }
    }

    private Task<Result<ProjectionBatchResult>> SubmitCheckpointAsync(DecodedGrainRequest request, PrincipalRecord principal,
        Command<DatabaseCommand> admitted, CancellationToken cancellationToken)
        => (GrainOnlineTextCheckpointCommand.TrySubmit(database, coordinator, request, principal,
            onlineText, clock, cancellationToken)).Value
            ?? coordinator.SubmitVerifiedAsync<ProjectionBatchResult>(admitted, cancellationToken);

    private static Exception MarkFailure(Exception error, GrainFailureStage stage)
    {
        if (GrainBoundaryErrors.Handles(error))
        {
            if (stage == GrainFailureStage.ReplyEncoding)
            {
                ((error.Data /* KLD0041: Use the CLR Exception.Data metadata slot for the closed private failure-stage enum while preserving the original exception and cause chain. */)[GrainFailureDiagnostics.StageMetadataKey] /* KLD0041: Use the CLR Exception.Data metadata slot for the closed private failure-stage enum while preserving the original exception and cause chain. */) = (stage /* KLD0042: The CLR Exception.Data slot stores the closed failure-stage enum only on the error path; validate that exact enum when reading it. */);
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
