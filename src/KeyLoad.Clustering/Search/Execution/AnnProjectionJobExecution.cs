using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Keeps job scheduling outside storage while charging each actual owner read to the original budget.</summary>
internal sealed class AnnProjectionJobExecution(BackgroundStorageClient storage, AnnProjectionJobDispatch dispatch)
{
    internal Task<AnnProjectionJobState?> PrepareAsync(SiloAddress target, ProjectionConsumerRef consumer,
        long generation, Guid nodeId, AnnProjectionJobState? current, ReadExecutionBudget budget,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
        => BackgroundStorageReadAccounting.ReadAsync<AnnProjectionJobState?>(budget, binding,
            cancellation => storage.Operations.PrepareAnnProjectionAsync(target, consumer, generation,
                nodeId, current, cancellation), allowAbsent: true, token: token);

    internal async Task<AnnMaintenanceResult> ExecuteAsync(IGrainFactory grains, SiloAddress target,
        AnnProjectionJobState original, ReadExecutionBudget budget,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var request = original.PendingRequest
            ?? throw Errors.Fail(ErrorCode.Validation, AnnProjectionJobProtocol.Invalid);
        var principal = await BackgroundStorageReadAccounting.ReadAsync<PrincipalRecord>(budget, binding,
            cancellation => storage.Operations.ReadAnnProjectionCreatorAsync(target, original,
                cancellation), token: token).ConfigureAwait(true);
        var result = await dispatch.ExecuteAsync(grains, principal, request, binding, token).ConfigureAwait(true);
        var validated = await BackgroundStorageReadAccounting.ReadAsync<bool>(budget, binding,
            cancellation => storage.Operations.ValidateAnnProjectionResultAsync(target, original,
                result, cancellation), token: token).ConfigureAwait(true);
        if (!validated)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProjectionJobProtocol.Invalid);
        }
        return result;
    }
}
