namespace KeyLoad.Comparisons;

internal static class OpenLoopAccountingValidator
{
    internal static OpenLoopOperationAccounting ValidateAndCreate(int notOffered, int harnessRejected,
        int timedOutBeforeStart, int succeeded, int failed, int targetRejected, int timedOutAfterStart,
        int unfinishedQueued, int unfinishedStarted, int started, int completed)
    {
        if (notOffered < 0 || harnessRejected < 0 || timedOutBeforeStart < 0 || succeeded < 0
            || failed < 0 || targetRejected < 0 || timedOutAfterStart < 0 || unfinishedQueued < 0
            || unfinishedStarted < 0 || started < 0 || completed < 0)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopAccountingInvariantFailed);
        }
        var accounting = new OpenLoopOperationAccounting(OpenLoopRateContract.PlannedOperations,
            notOffered, harnessRejected, timedOutBeforeStart, succeeded, failed, targetRejected,
            timedOutAfterStart, unfinishedQueued, unfinishedStarted, started, completed);
        var planned = checked(notOffered + harnessRejected + timedOutBeforeStart + succeeded + failed
            + targetRejected + timedOutAfterStart + unfinishedQueued + unfinishedStarted);
        var startedExpected = checked(succeeded + failed + targetRejected + timedOutAfterStart + unfinishedStarted);
        var completedExpected = checked(succeeded + failed + targetRejected + timedOutAfterStart);
        if (planned != accounting.Planned || started != startedExpected || completed != completedExpected
            || started > accounting.Planned || completed > started)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopAccountingInvariantFailed);
        }
        return accounting;
    }
}
