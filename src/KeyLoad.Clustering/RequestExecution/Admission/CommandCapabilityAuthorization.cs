using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Performs fresh command authority reads inside the original target observation budget.</summary>
internal static class CommandCapabilityAuthorization
{
    internal static async Task<PrincipalRecord> AuthorizeAsync(DatabaseEngine database, TimeProvider clock,
        GrainRequestCodec? codec, DecodedGrainRequest request, IGrainContext? context, CancellationToken token)
    {
        try
        {
            ValidateFresh(database, request, token);
            await ObserveAndValidateAsync(database, codec, request,
                GrainRequestPhase.AuthorizationReload, context, token);
            return GrainRequestAuthority.ReloadForRequest(database, request.Envelope, clock);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            GrainFailureDiagnostics.Mark(error, GrainFailureStage.Authorization);
            throw;
        }
    }

    internal static async ValueTask ObserveAndValidateAsync(DatabaseEngine database,
        GrainRequestCodec? codec, DecodedGrainRequest request,
        GrainRequestPhase phase, IGrainContext? context, CancellationToken token)
    {
        if (codec is not { HasPhaseObserver: true } observerCodec)
        {
            return;
        }
        await observerCodec.ObservePhaseAsync(request, phase, context, token);
        ValidateFresh(database, request, token);
    }

    private static void ValidateFresh(DatabaseEngine database, DecodedGrainRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        GrainRequestScope.Validate(request.Envelope, database.Store.Identity.Incarnation);
        GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
    }
}
