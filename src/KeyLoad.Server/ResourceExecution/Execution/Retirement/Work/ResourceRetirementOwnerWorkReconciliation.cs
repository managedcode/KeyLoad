using ManagedCode.Communication.CQRS;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Uses actual native tail materialization and fresh persisted evidence; never retries with invented work.</summary>
internal static class ResourceRetirementOwnerWorkReconciliation
{
    internal static async Task<ResourceRetirementOwnerWorkSubmission> SubmitAsync(
        Func<Task<ResourceRetirementPhaseResultV1>> submit, Func<Task<ResourceRetirementWorkCaptureResult>> readDurable,
        Action<ResourceRetirementWorkCaptureResult> requireDurable, ResourceRetirementWorkCaptureResult original)
    {
        try
        {
            var result = await submit().ConfigureAwait(false);
            var receipt = result.WorkOwnerReceipt
                ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            ResourceRetirementWorkOwnerCapture.RequireOriginalCohort(original, receipt);
            return new(receipt, null, null);
        }
        catch (KeyLoadException primary) when (primary.Code == ErrorCode.Conflict
            && CqrsRuntimeFailures.FindFatal(primary) is null)
        {
            var failures = new List<Exception> { primary };
            ResourceRetirementWorkCaptureResult? recovered = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var observed = await readDurable().ConfigureAwait(false);
                var receipt = observed.Acknowledged
                    ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
                requireDurable(observed);
                ResourceRetirementWorkOwnerCapture.RequireOriginalCohort(original, receipt);
                recovered = observed;
            }, failures).ConfigureAwait(false);
            if (recovered is null)
            {
                ServerFailureObserver.ThrowIfAny(failures);
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
            }
            if (CqrsRuntimeFailures.FindFatal(primary) is not null)
            {
                ServerFailureObserver.ThrowIfAny(failures);
            }
            return new(recovered.Acknowledged!, recovered, primary);
        }
    }
}
