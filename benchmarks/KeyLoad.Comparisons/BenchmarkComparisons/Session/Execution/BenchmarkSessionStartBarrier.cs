
namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionStartBarrier(int participants, TimeProvider clock)
{
    private readonly TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int ready;
    internal long Origin
    {
        get; private set;
    }
    internal Task ReadyAsync()
    {
        if (Interlocked.Increment(ref ready) == participants)
        {
            Origin = clock.GetTimestamp();
            start.TrySetResult();
        }
        return start.Task;
    }
    internal void Abort(Exception error) => start.TrySetException(error);
}
