using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal sealed class GrainCommandExecutor(DatabaseEngine database, ICommitCoordinator coordinator, TimeProvider clock,
    GrainRequestCodec? codec = null)
{
    internal async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request, string actorKey,
        CancellationToken cancellationToken, IGrainContext? context = null)
    {
        var stage = GrainFailureStage.PartitionResolution;
        try
        {
            ValidateRoute(request, actorKey, cancellationToken);
            stage = GrainFailureStage.QuorumRead;
            await coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(true);
            ValidateFreshRequest(request, cancellationToken);
            var envelope = request.Envelope;
            stage = GrainFailureStage.Authorization;
            await ObserveAndValidateRequestAsync(request, GrainRequestPhase.AuthorizationReload, context,
                cancellationToken).ConfigureAwait(true);
            var principal = GrainRequestAuthority.Reload(database, envelope.PrincipalId!, clock);
            var kind = envelope.CommandKind ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            stage = GrainFailureStage.CapabilityExecution;
            await ObserveAndValidateRequestAsync(request, GrainRequestPhase.BeforeSubmit, context,
                cancellationToken).ConfigureAwait(true);
            var result = await coordinator.SubmitNativeAsync(kind, envelope.CommandId, principal.Id,
                request.Payload, cancellationToken).ConfigureAwait(true);
            await ObservePhaseAsync(request, GrainRequestPhase.SubmitReturned, context, cancellationToken)
                .ConfigureAwait(true);
            stage = GrainFailureStage.ReplyEncoding;
            return GrainReplyFactory.Operation(result, cancellationToken);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            GrainFailureDiagnostics.Mark(error, stage);
            throw;
        }
    }

    private static void ValidateRoute(DecodedGrainRequest request, string actorKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
        if (GrainPartitionResolver.Resolve(request) != actorKey)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
    }

    private void ValidateFreshRequest(DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestScope.Validate(request.Envelope, database.Store.Identity.Incarnation, clock.GetUtcNow());
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
