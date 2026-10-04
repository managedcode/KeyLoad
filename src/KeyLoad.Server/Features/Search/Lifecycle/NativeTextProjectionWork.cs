namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjectionWork
{
    private TaskCompletionSource<bool> signal = CompletedSignal();
    private int activeLeases;
    private int activeCleanups;

    internal int ActiveLeases => activeLeases;
    internal bool IsQuiet => activeLeases == 0 && activeCleanups == 0;
    internal Task Signal => signal.Task;

    internal void StartLease()
    {
        ResetWhenQuiet();
        activeLeases++;
    }

    internal void EndLease()
    {
        activeLeases--;
        CompleteWhenQuiet();
    }

    internal void StartCleanup()
    {
        ResetWhenQuiet();
        activeCleanups++;
    }

    internal void EndCleanup()
    {
        activeCleanups--;
        CompleteWhenQuiet();
    }

    private void ResetWhenQuiet()
    {
        if (IsQuiet)
        {
            signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void CompleteWhenQuiet()
    {
        if (IsQuiet)
        {
            signal.TrySetResult(true);
        }
    }

    private static TaskCompletionSource<bool> CompletedSignal()
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        completed.SetResult(true);
        return completed;
    }
}
