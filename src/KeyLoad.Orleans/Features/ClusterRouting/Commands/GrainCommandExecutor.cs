using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class GrainCommandExecutor(DatabaseEngine database, ICommitCoordinator coordinator, TimeProvider clock,
    IOptions<GrainRoutingOptions> options, GrainRequestCodec? codec = null, NativeRequestWorkOwner? workOwner = null, IPhysicalRequestPlacement? physical = null,
    IGrainPartitionMovementSealedOperationObserver? sealedObserver = null)
{
    private readonly GrainRoutingOptions settings = options.Value;
    internal async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request, string actorKey,
        CancellationToken cancellationToken, IGrainContext? context = null)
    {
        var stage = GrainFailureStage.PartitionResolution;
        NativeCapabilityWorkLifetime? work = null;
        Exception? primaryError = null;
        try
        {
            ValidateRoute(request, actorKey, cancellationToken);
            stage = GrainFailureStage.CapabilityExecution;
            work = NativeCapabilityWorkLifetime.Acquire(workOwner, request.Envelope.RequestId,
                NativeRequestWorkKind.CommandCapability, cancellationToken);
            var operationToken = work?.Token ?? cancellationToken;
            stage = GrainFailureStage.QuorumRead;
            await coordinator.ReadBarrierAsync(operationToken).ConfigureAwait(true);
            ValidateFreshRequest(request, operationToken);
            var envelope = request.Envelope;
            stage = GrainFailureStage.Authorization;
            await ObserveAndValidateRequestAsync(request, GrainRequestPhase.AuthorizationReload, context,
                operationToken).ConfigureAwait(true);
            var principal = GrainRequestAuthority.ReloadForRequest(database, envelope, clock);
            if (physical is not null)
            {
                database.VerifyPhysicalCommandOwner(physical.Owner,
                request.Envelope.CommandKind == OperationKind.BootstrapPhysicalShardCatalog, operationToken);
            }
            var kind = envelope.CommandKind ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            stage = GrainFailureStage.CapabilityExecution;
            await ObserveAndValidateRequestAsync(request, GrainRequestPhase.BeforeSubmit, context,
                operationToken).ConfigureAwait(true);
            var result = kind == OperationKind.PartitionMovementPhase
                ? await GrainPartitionMovementCommand.SubmitAsync(database, coordinator, request, principal,
                    codec, context, sealedObserver, operationToken).ConfigureAwait(true)
                : await coordinator.SubmitNativeAsync(kind, envelope.CommandId, principal.Id,
                    request.Payload, operationToken).ConfigureAwait(true);
            await ObservePhaseAsync(request, GrainRequestPhase.SubmitReturned, context, operationToken)
                .ConfigureAwait(true);
            stage = GrainFailureStage.ReplyEncoding;
            return GrainReplyFactory.Operation(result: result, cancellationToken: operationToken, options: options);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            primaryError = MarkFailure(error, stage);
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            primaryError = error;
            throw;
        }
        finally
        {
            using var cleanup = work;
            NativeCapabilityWorkLifetime.Settle(primaryError, work, null, null);
        }
    }

    private static Exception MarkFailure(Exception error, GrainFailureStage stage)
    {
        if (GrainBoundaryErrors.Handles(error))
        {
            GrainFailureDiagnostics.Mark(error, stage);
        }
        return error;
    }

    private void ValidateRoute(DecodedGrainRequest request, string actorKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
        PhysicalCommandActorKey.Validate(request, actorKey, physical);
    }

    private void ValidateFreshRequest(DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestScope.Validate(request.Envelope, database.Store.Identity.Incarnation, clock.GetUtcNow(), settings.MaximumFuture);
        GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
    }

    private async ValueTask ObserveAndValidateRequestAsync(DecodedGrainRequest request, GrainRequestPhase phase,
        IGrainContext? context, CancellationToken cancellationToken)
    {
        if (codec is not { HasPhaseObserver: true } observerCodec)
        {
            return;
        }

        await observerCodec.ObservePhaseAsync(request, phase, context, cancellationToken).ConfigureAwait(true);
        ValidateFreshRequest(request, cancellationToken);
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
