using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Charges receiver admission reads without altering the existing issued operation or page DATA grant.</summary>
internal static class NativeCommandMetadataExecution
{
    internal static ReadExecutionBudget CreateWork(DatabaseEngine database, DecodedGrainRequest request,
        TimeProvider clock, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var admission = request.Envelope.Metadata;
        NativeCommandMetadataAdmission.Require(admission, database.Limits);
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        work.ConstrainLifetime(request.Envelope.ExpiresAt);
        work.ConstrainNativeWork(admission!.MaximumReadBytes, admission.MaximumExaminedRecords);
        work.ConstrainResultBytes(admission.MaximumResultBytes);
        work.AttachResourceLifetimeBinding(binding);
        return work;
    }

    internal static async Task<OperationResult> ExecuteAsync(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, IServiceProvider services, TimeProvider clock, GrainRequestCodec codec,
        global::Orleans.Runtime.IGrainContext context, ReadExecutionBudget work,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        work.Check();
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
        await ObserveAsync(codec, request, GrainRequestPhase.AuthorizationReload, context, work, token).ConfigureAwait(true);
        var principal = GrainRequestAuthority.ReloadForRequest(database, request.Envelope, clock, work);
        GrainRequestAuthority.RequireAdministrator(principal);
        var actualOwner = services.GetRequiredService<PhysicalShardRecord>();
        database.VerifyPhysicalCommandOwner(actualOwner, false, work, token);
        var original = GrainResourceRetirementCommand.Verify(database, request, principal);
        if (codec.PhaseObserver is IGrainNativeOperationPhaseObserver nativeObserver)
        {
            nativeObserver.ObserveNativeOperation(GrainRequestProbeIdentity.From(request.Envelope),
                original with
                {
                    NativePayload = original.NativePayload.ToArray()
                });
        }
        await ObserveAsync(codec, request, GrainRequestPhase.BeforeSubmit, context, work, token).ConfigureAwait(true);
        database.ValidateResourceEffectSubmission(original, clock.GetUtcNow(), binding, work);
        var result = await GrainResourceRetirementCommand.SubmitAsync(database, coordinator, request, principal, work, token)
            .ConfigureAwait(true);
        await ObserveAsync(codec, request, GrainRequestPhase.SubmitReturned, context, work, token).ConfigureAwait(true);
        return result;
    }

    private static async ValueTask ObserveAsync(GrainRequestCodec codec, DecodedGrainRequest request,
        GrainRequestPhase phase, global::Orleans.Runtime.IGrainContext context, ReadExecutionBudget work,
        CancellationToken token)
    {
        work.Check();
        if (codec.HasPhaseObserver)
        {
            await codec.ObservePhaseAsync(request, phase, context, token).ConfigureAwait(true);
        }
        codec.ValidateScope(request.Envelope);
        work.Check();
    }
}
