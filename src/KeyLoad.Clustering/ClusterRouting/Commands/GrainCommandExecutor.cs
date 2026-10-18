using KeyLoad.Core;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal sealed class GrainCommandExecutor(DatabaseEngine database, ICommitCoordinator coordinator, TimeProvider clock,
    IOptions<GrainRoutingOptions> options, GrainRequestCodec? codec = null, NativeRequestWorkOwner? workOwner = null,
    INativeOnlineTextMaintenance? onlineText = null, Func<INativeDropTableExecution>? borrowDropTable = null)
{
    private readonly GrainRoutingOptions settings = options.Value;
    internal async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request, string actorKey,
        CancellationToken cancellationToken, IGrainContext? context = null)
    {
        var stage = GrainFailureStage.PartitionResolution;
        var coreEntered = false;
        try
        {
            ValidateRoute(request, actorKey, cancellationToken);
            stage = GrainFailureStage.CapabilityExecution;
            await using var work = NativeCapabilityWorkLifetime.Acquire(workOwner, request.Envelope.RequestId,
                NativeRequestWorkKind.CommandCapability, database.OperationLimitsOptions, cancellationToken);
            coreEntered = true;
            return await ExecuteCoreAsync(request, work, context,
                work?.Token ?? cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            var primaryError = coreEntered ? error : MarkFailure(error, stage);
            NativeRequestWorkSettlement.Rethrow(primaryError);
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            NativeRequestWorkSettlement.Rethrow(error);
            throw;
        }
    }

    private async Task<GrainOperationReply> ExecuteCoreAsync(DecodedGrainRequest request,
        NativeCapabilityWorkLifetime? work, IGrainContext? context, CancellationToken operationToken)
    {
        var stage = GrainFailureStage.CapabilityExecution;
        try
        {
            var principal = await NativeCommandCapabilityAuthorization.AuthorizeAsync(database, coordinator,
                clock, settings, codec, request, context, operationToken).ConfigureAwait(true);
            var kind = request.Envelope.CommandKind
                ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            await NativeCommandCapabilityAuthorization.ObserveAndValidateAsync(database, clock, settings,
                codec, request, GrainRequestPhase.BeforeSubmit, context, operationToken).ConfigureAwait(true);
            NativeCommandWorkAdmission.Bind(database, request, principal, clock, work);
            var result = await SubmitCommandAsync(kind, request, principal, operationToken).ConfigureAwait(true);
            stage = GrainFailureStage.ReplyEncoding;
            await ObservePhaseAsync(request, GrainRequestPhase.SubmitReturned, context, operationToken).ConfigureAwait(true);
            var reply = GrainReplyFactory.Operation(result: result, cancellationToken: operationToken, options: options);
            return work is null || reply.Error is not null ? reply : reply with
            {
                WorkProof = NativeCommandWorkAdmission.Capture(request, work, principal.PolicyEpoch)
            };
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            var primaryError = MarkFailure(error, stage);
            work?.PrimaryError = primaryError;
            NativeRequestWorkSettlement.Rethrow(primaryError);
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            work?.PrimaryError = error;
            NativeRequestWorkSettlement.Rethrow(error);
            throw;
        }
    }

    private async Task<Result<object>> SubmitCommandAsync(OperationKind kind, DecodedGrainRequest request,
        PrincipalRecord principal, CancellationToken operationToken)
    {
        return kind switch
        {
            OperationKind.DropTable => await (borrowDropTable?.Invoke()
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest))
                .ExecuteAsync(request.Envelope.RequestId, request.Envelope.CommandId, principal.Id,
                    GrainNativePayload.Read<DropTableRequest>(request.Payload), request.Envelope.ExpiresAt, operationToken).ConfigureAwait(true),
            OperationKind.Batch => await SubmitBatchAsync(request, principal, operationToken).ConfigureAwait(true),
            OperationKind.CommitProjectionBatch => await SubmitCheckpointAsync(request, principal,
                operationToken).ConfigureAwait(true),
            OperationKind.OnlineTextPublicationPhase => await GrainOnlineTextPublicationCommand.SubmitAsync(
                database, coordinator, request, principal, onlineText, operationToken).ConfigureAwait(true),
            OperationKind.Bm25CorpusPublicationPhase => await GrainBm25CorpusPublicationCommand.SubmitAsync(
                database, coordinator, request, principal, operationToken).ConfigureAwait(true),
            _ => await coordinator.SubmitNativeAsync(kind, request.Envelope.CommandId, principal.Id,
                request.Payload, cancellationToken: operationToken).ConfigureAwait(true)
        };
    }

    private Task<Result<object>> SubmitBatchAsync(DecodedGrainRequest request, PrincipalRecord principal,
        CancellationToken token)
        => coordinator.SubmitNativeAsync(OperationKind.Batch, request.Envelope.CommandId,
                principal.Id, request.Payload, cancellationToken: token);

    private Task<Result<object>> SubmitCheckpointAsync(DecodedGrainRequest request, PrincipalRecord principal,
        CancellationToken cancellationToken)
        => GrainOnlineTextCheckpointCommand.TrySubmit(database, coordinator, request, principal,
            onlineText, clock, cancellationToken)
            ?? coordinator.SubmitNativeAsync(OperationKind.CommitProjectionBatch, request.Envelope.CommandId,
                principal.Id, request.Payload, cancellationToken: cancellationToken);

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
