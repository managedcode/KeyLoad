namespace KeyLoad.Comparisons;

/// <summary>Keeps elapsed measurements on the owning provider's monotonic timeline.</summary>
internal sealed class ComparisonElapsedMeasurement(TimeProvider timeProvider)
{
    private readonly long started = timeProvider.GetTimestamp();
    private TimeSpan? elapsed;

    internal TimeSpan Elapsed => elapsed ?? timeProvider.GetElapsedTime(started);
    internal long ElapsedMilliseconds => (long)Elapsed.TotalMilliseconds;
    internal TimeProvider TimeProvider => timeProvider;

    internal void Stop() => elapsed ??= timeProvider.GetElapsedTime(started);
}
