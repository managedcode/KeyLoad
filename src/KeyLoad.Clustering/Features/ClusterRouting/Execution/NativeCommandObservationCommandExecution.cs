using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Consumes one pre-effect claim without recreating its intent, budget or native command owner.</summary>
internal static class NativeCommandObservationCommandExecution
{
    internal static async Task<T> ExecuteAsync<T>(DatabaseEngine database, ICommitCoordinator coordinator,
        TimeProvider clock, GrainRoutingOptions settings, GrainRequestCodec codec,
        IPhysicalRequestPlacement physical, ReplicaSiloDiscoveryState actualRuntime, NativeCommandObservation original,
        Func<CancellationToken, Task> admitPeer, NativeCommandObservationPublication<T> publication, CancellationToken token)
    {
        CancellationTokenSource? linked = null;
        ReadExecutionBudgetStageCancellationLease? stage = null;
        Exception? failure = null;
        try
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, token);
            stage = original.Budget.EnterStageCancellation(linked.Token);
            original.Budget.Check();
            await admitPeer(linked.Token).ConfigureAwait(true);
            original.Budget.Check();
            await SubmitAndPublishAsync(database, coordinator, clock, settings, codec, physical,
                actualRuntime, original, publication, linked.Token).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            failure = error;
        }
        failure = Dispose(stage, failure);
        failure = Dispose(linked, failure);
        try
        {
            await original.FinishCommandExecutionAsync(failure).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            failure = NativeCommandObservationCompletion.Retain(failure, error);
        }
        if (failure is not null)
        {
            try
            {
                await original.AbortAsync(failure).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                failure = NativeCommandObservationCompletion.Retain(failure, error);
            }
            NativeRequestWorkSettlement.Rethrow(publication.Clear(failure!));
        }
        return publication.Value;
    }

    private static async Task SubmitAndPublishAsync<T>(DatabaseEngine database, ICommitCoordinator coordinator,
        TimeProvider clock, GrainRoutingOptions settings, GrainRequestCodec codec,
        IPhysicalRequestPlacement physical, ReplicaSiloDiscoveryState actualRuntime, NativeCommandObservation original,
        NativeCommandObservationPublication<T> publication, CancellationToken token)
    {
        var request = original.Request;
        original.Budget.Check();
        var principal = await NativeCommandCapabilityAuthorization.AuthorizeNativeAsync(database, coordinator,
            clock, settings, codec, physical, request, original, actualRuntime, token).ConfigureAwait(true);
        await NativeCommandCapabilityAuthorization.ObserveNativeAndValidateAsync(database, clock, settings, codec,
            request, GrainRequestPhase.BeforeSubmit, actualRuntime, token).ConfigureAwait(true);
        database.ValidateResourceEffectSubmission(original.Intent, clock.GetUtcNow(),
            original.CommandWork, original.Budget);
        _ = await coordinator.SubmitVerifiedAsync(original.Intent, token, original.Budget).ConfigureAwait(true);
        await codec.ObserveNativePhaseAsync(request, GrainRequestPhase.SubmitReturned, actualRuntime, token).ConfigureAwait(true);
        var reply = new GrainOperationReply
        {
            WorkProof = NativeCommandWorkAdmission.Capture(request, original.CommandWork, principal.PolicyEpoch) with
            {
                ObservationToken = original.SignedToken
            }
        };
        original.Budget.CheckResult(reply.WorkProof);
        _ = publication.Capture(reply, original.Budget);
        original.Budget.Check();
        original.Lease.AdmitPublication();
    }

    private static Exception? Dispose(IDisposable? resource, Exception? previous)
    {
        try
        {
            resource?.Dispose();
        }
        catch (Exception error)
        {
            return NativeCommandObservationCompletion.Retain(previous, error);
        }
        return previous;
    }
}
