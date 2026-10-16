namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionTimer(TimeProvider clock)
{
    private readonly long started = clock.GetTimestamp();
    internal TimeSpan Elapsed => clock.GetElapsedTime(started);
}
