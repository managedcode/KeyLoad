using System.Runtime.ExceptionServices;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeDropTablePageFailure
{
    internal static async Task ObserveAsync(NativeDropTableContext context, ResourceRetirementAttemptContextV1 scope,
        ResourceRetirementAttemptV1 attempt, ResourceRetirementPreparedOperations operations, Exception primary)
    {
        if (primary is not KeyLoadException domain || domain.Code == ErrorCode.UnknownWriteOutcome
            || context.Token.IsCancellationRequested)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
        var failures = new List<Exception> { primary };
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            // Only the canonical closed receipt can prove definite failure; admission errors are never server-side evidence.
            _ = await operations.CloseAsync(scope, attempt, scope.Receiver!, context.PrincipalId, context.PolicyEpoch,
                context.Expiry, context.Work, context.Metadata, context.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
