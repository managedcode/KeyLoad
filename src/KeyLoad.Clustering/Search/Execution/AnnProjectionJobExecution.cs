using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Keeps job scheduling outside storage while charging each actual owner read to the original budget.</summary>
internal sealed class AnnProjectionJobExecution(BackgroundStorageClient storage, AnnProjectionJobDispatch dispatch)
{
    internal Task<AnnProjectionJobState?> PrepareAsync(SiloAddress target, ProjectionConsumerRef consumer,
        long generation, Guid nodeId, AnnProjectionJobState? current, ReadExecutionBudget budget,
        DateTimeOffset expiry, IResourceLifetimeWorkBinding binding, CancellationToken token)
        => BackgroundStorageReadAccounting.ReadAsync<AnnProjectionJobState?>(budget, binding, expiry,
            (bounds, cancellation) => storage.Operations.PrepareAnnProjectionAsync(target, consumer, generation,
                nodeId, current, bounds, cancellation), allowAbsent: true, token: token);

    internal async Task<AnnMaintenanceResult> ExecuteAsync(IGrainFactory grains, SiloAddress target,
        AnnProjectionJobState original, ReadExecutionBudget budget, DateTimeOffset expiry,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var request = original.PendingRequest
            ?? throw Errors.Fail(ErrorCode.Validation, AnnProjectionJobProtocol.Invalid);
        var principal = await BackgroundStorageReadAccounting.ReadAsync<PrincipalRecord>(budget, binding, expiry,
            (bounds, cancellation) => storage.Operations.ReadAnnProjectionCreatorAsync(target, original,
                bounds, cancellation), token: token).ConfigureAwait(true);
        var result = await dispatch.ExecuteAsync(grains, principal, request, expiry, binding, token).ConfigureAwait(true);
        var validated = await BackgroundStorageReadAccounting.ReadAsync<bool>(budget, binding, expiry,
            (bounds, cancellation) => storage.Operations.ValidateAnnProjectionResultAsync(target, original,
                result, bounds, cancellation), token: token).ConfigureAwait(true);
        if (!validated)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProjectionJobProtocol.Invalid);
        }
        return result;
    }
}
