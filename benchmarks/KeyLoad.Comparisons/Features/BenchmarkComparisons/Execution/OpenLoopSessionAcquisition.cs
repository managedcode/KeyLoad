using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace KeyLoad.Comparisons;

internal static class OpenLoopSessionAcquisition
{
    internal static async Task<List<IComparisonSession>> OpenAsync(IComparisonTarget target,
        OpenLoopExecutionPolicy policy, CancellationToken cancellationToken)
    {
        const int FirstElementIndex = 0;

        var sessions = new List<IComparisonSession>(policy.ConcurrentSessions);
        try
        {
            for (var index = FirstElementIndex; index < policy.ConcurrentSessions; index++)
            {
                sessions.Add(await target.OpenSessionAsync(cancellationToken).ConfigureAwait(false));
            }
            return sessions;
        }
        catch (Exception primary)
        {
            var failures = await SettlePartialSessionsAsync(sessions, policy).ConfigureAwait(false);
            ExceptionDispatchInfo.Capture(OpenLoopFailure.Combine(primary, failures)!).Throw();
            throw;
        }
    }

    private static async Task<ImmutableArray<Exception>> SettlePartialSessionsAsync(
        List<IComparisonSession> sessions, OpenLoopExecutionPolicy policy)
    {
        var original = ComparisonSessionCleanup.CloseAndJoinAsync(sessions,
            TimeSpan.FromMilliseconds(policy.DrainMilliseconds));
        try
        {
            var close = await original.ConfigureAwait(false);
            return close.ThresholdExpired
                ? close.Failures.Add(new ComparisonFailureException(ComparisonSessionCleanup.Failure))
                : close.Failures;
        }
        catch (Exception failure) when (original.IsFaulted || original.IsCanceled)
        {
            return [failure];
        }
    }
}
