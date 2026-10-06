namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodePipeFailureOwnerEvidence
{
    private const string MissingOutputFailure = "The real Node pipe did not exceed its configured output bound.";
    internal const string MissingAggregate = "The owning cleanup path did not retain both actual pipe failures.";

    internal static T InvokeNative<T>(Func<T> operation)
    {
        try
        {
            return IsolatedAggregateNodeGuardedInvocation.Invoke(operation);
        }
        catch (AggregateException envelope)
        {
            throw IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
        }
    }

    internal static async Task<InvalidOperationException> CaptureLimitFailureAsync(Task<string> reader)
    {
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(() => reader);
        }
        catch (AggregateException envelope)
        {
            if (IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope) is InvalidOperationException failure
                && failure.Message == IsolatedAggregateNodeLifetimeProgram.OutputLimitMessage)
            {
                return failure;
            }
            throw IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
        }
        throw new InvalidOperationException(MissingOutputFailure);
    }

    internal static AggregateException CaptureCombinedFailures(IsolatedAggregateNodeFailureSet failures)
    {
        try
        {
            failures.Throw(primary: null);
        }
        catch (AggregateException failure)
        {
            return failure;
        }
        throw new InvalidOperationException(MissingAggregate);
    }
}
