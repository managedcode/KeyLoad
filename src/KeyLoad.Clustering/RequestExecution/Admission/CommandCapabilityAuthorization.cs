using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Performs fresh command authority reads inside the original target observation budget.</summary>
internal static class CommandCapabilityAuthorization
{
    internal static async Task<PrincipalRecord> AuthorizeAsync(DatabaseEngine database,
        ICommitCoordinator coordinator, TimeProvider clock, GrainRoutingOptions settings,
        GrainRequestCodec? codec, DecodedGrainRequest request, IGrainContext? context, CancellationToken token)
    {
        var stage = GrainFailureStage.ReadBarrier;
        try
        {
            await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
            ValidateFresh(database, clock, settings, request, token);
            stage = GrainFailureStage.Authorization;
            await ObserveAndValidateAsync(database, clock, settings, codec, request,
                GrainRequestPhase.AuthorizationReload, context, token).ConfigureAwait(true);
            return GrainRequestAuthority.ReloadForRequest(database, request.Envelope, clock);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            GrainFailureDiagnostics.Mark(error, stage);
            throw;
        }
    }

    internal static async ValueTask ObserveAndValidateAsync(DatabaseEngine database, TimeProvider clock,
        GrainRoutingOptions settings, GrainRequestCodec? codec, DecodedGrainRequest request,
        GrainRequestPhase phase, IGrainContext? context, CancellationToken token)
    {
        if (codec is not { HasPhaseObserver: true } observerCodec)
        {
            return;
        }
        await observerCodec.ObservePhaseAsync(request, phase, context, token).ConfigureAwait(true);
        ValidateFresh(database, clock, settings, request, token);
    }

    private static void ValidateFresh(DatabaseEngine database, TimeProvider clock, GrainRoutingOptions settings,
        DecodedGrainRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        GrainRequestScope.Validate(request.Envelope, database.Store.Identity.Incarnation,
            clock.GetUtcNow(), settings.MaximumFuture);
        GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
    }
}
