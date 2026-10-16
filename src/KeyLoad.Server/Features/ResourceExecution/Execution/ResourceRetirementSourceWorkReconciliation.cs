using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed record ResourceRetirementSourceWorkSubmission(ResourceRetirementStateV1 State,
    Exception? OriginalSubmissionFailure);

internal static class ResourceRetirementSourceWorkReconciliation
{
    internal static async Task<ResourceRetirementSourceWorkSubmission> SubmitAsync(
        Func<Task<ResourceRetirementPhaseResultV1>> submit, Func<Task<ResourceRetirementStateV1>> readCurrent,
        ResourceRetirementWorkOwnerReceiptV1 original, DatabaseLimits limits)
    {
        try
        {
            var result = await submit().ConfigureAwait(false);
            var state = result.State ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            RequireRecorded(state, original, limits);
            return new(state, null);
        }
        catch (KeyLoadException primary) when (primary.Code == ErrorCode.Conflict
            && CqrsRuntimeFailures.FindFatal(primary) is null)
        {
            var failures = new List<Exception> { primary };
            ResourceRetirementStateV1? recovered = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var state = await readCurrent().ConfigureAwait(false);
                RequireRecorded(state, original, limits);
                recovered = state;
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
            return new(recovered, primary);
        }
    }

    private static void RequireRecorded(ResourceRetirementStateV1 state,
        ResourceRetirementWorkOwnerReceiptV1 original, DatabaseLimits limits)
    {
        ResourceRetirementStateValidation.State(state, limits);
        if (!ResourceRetirementValidation.Same(state.Control, original.Control)
            || !state.WorkOwnerReceipts.Any(item => ResourceRetirementValidation.Same(item, original)))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
    }
}
