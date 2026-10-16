using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Performs fresh command authority reads inside the original target observation budget.</summary>
internal static class NativeCommandCapabilityAuthorization
{
    internal static Task<PrincipalRecord> AuthorizeAsync(DatabaseEngine database,
        ICommitCoordinator coordinator, TimeProvider clock, GrainRoutingOptions settings,
        GrainRequestCodec? codec, IPhysicalRequestPlacement? physical, DecodedGrainRequest request,
        NativeCommandObservation? observation, IGrainContext? context, CancellationToken token)
        => AuthorizeCoreAsync(database, coordinator, clock, settings, codec, physical, request,
            observation, context, null, token);

    internal static Task<PrincipalRecord> AuthorizeNativeAsync(DatabaseEngine database,
        ICommitCoordinator coordinator, TimeProvider clock, GrainRoutingOptions settings,
        GrainRequestCodec codec, IPhysicalRequestPlacement physical, DecodedGrainRequest request,
        NativeCommandObservation observation, ReplicaSiloDiscoveryState actualRuntime, CancellationToken token)
        => AuthorizeCoreAsync(database, coordinator, clock, settings, codec, physical, request,
            observation, null, actualRuntime, token);

    private static async Task<PrincipalRecord> AuthorizeCoreAsync(DatabaseEngine database,
        ICommitCoordinator coordinator, TimeProvider clock, GrainRoutingOptions settings,
        GrainRequestCodec? codec, IPhysicalRequestPlacement? physical, DecodedGrainRequest request,
        NativeCommandObservation? observation, IGrainContext? context, ReplicaSiloDiscoveryState? nativeRuntime, CancellationToken token)
    {
        var stage = GrainFailureStage.QuorumRead;
        try
        {
            await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
            ValidateFresh(database, clock, settings, request, token);
            stage = GrainFailureStage.Authorization;
            if (nativeRuntime is null)
            {
                await ObserveAndValidateAsync(database, clock, settings, codec, request,
                    GrainRequestPhase.AuthorizationReload, context, token).ConfigureAwait(true);
            }
            else
            {
                await ObserveNativeAndValidateAsync(database, clock, settings, codec!, request,
                    GrainRequestPhase.AuthorizationReload, nativeRuntime, token).ConfigureAwait(true);
            }
            var principal = observation is null
                ? GrainRequestAuthority.ReloadForRequest(database, request.Envelope, clock)
                : GrainRequestAuthority.ReloadForRequest(database, request.Envelope, clock, observation.Budget);
            if (physical is not null)
            {
                var bootstrap = request.Envelope.CommandKind == OperationKind.BootstrapPhysicalShardCatalog;
                if (observation is null)
                {
                    database.VerifyPhysicalCommandOwner(physical.Owner, bootstrap, token);
                }
                else
                {
                    database.VerifyPhysicalCommandOwner(physical.Owner, bootstrap, observation.Budget, token);
                }
            }
            return principal;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
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

    internal static async ValueTask ObserveNativeAndValidateAsync(DatabaseEngine database, TimeProvider clock,
        GrainRoutingOptions settings, GrainRequestCodec codec, DecodedGrainRequest request,
        GrainRequestPhase phase, ReplicaSiloDiscoveryState actualRuntime, CancellationToken token)
    {
        await codec.ObserveNativePhaseAsync(request, phase, actualRuntime, token).ConfigureAwait(true);
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
