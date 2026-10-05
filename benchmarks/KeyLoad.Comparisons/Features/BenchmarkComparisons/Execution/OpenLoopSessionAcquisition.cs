using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace KeyLoad.Comparisons;

internal static class OpenLoopSessionAcquisition
{
    internal static async Task<List<IComparisonSession>> OpenAsync(IComparisonTarget target,
        OpenLoopExecutionPolicy policy, CancellationToken cancellationToken)
    {
        var sessions = new List<IComparisonSession>(policy.ConcurrentSessions);
        try
        {
            for (var index = 0; index < policy.ConcurrentSessions; index++)
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
        try
        {
            var close = await ComparisonSessionCleanup.CloseAndJoinAsync(sessions,
                TimeSpan.FromMilliseconds(policy.DrainMilliseconds)).ConfigureAwait(false);
            return close.ThresholdExpired
                ? close.Failures.Add(new ComparisonFailureException(ComparisonSessionCleanup.Failure))
                : close.Failures;
        }
        catch (Exception failure)
        {
            return [failure];
        }
    }
}
