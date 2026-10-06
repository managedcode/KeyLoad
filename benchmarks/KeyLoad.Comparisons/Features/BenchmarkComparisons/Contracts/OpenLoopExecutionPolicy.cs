
namespace KeyLoad.Comparisons;

/// <summary>Records the exact immutable runtime options consumed by one separate open-loop artifact.</summary>
public sealed record OpenLoopExecutionPolicy(int QueueCapacity, int ConcurrentSessions, int MaximumNodes,
    int OperationDeadlineMilliseconds, int DrainMilliseconds, int ControlPollMilliseconds,
    int SpinWindowMicroseconds)
{
    private const int MillisecondsPerSecond = 1_000;
    private const int MicrosecondsPerSecond = 1_000_000;
    private const int CeilingRoundingAdjustment = 1;

    /// <summary>Checks that evidence carries the single currently qualified v1 policy.</summary>
    /// <returns>Whether all seven values match the canonical options definition.</returns>
    internal bool IsQualifiedV1()
        => QueueCapacity == OpenLoopExecutionOptions.DefaultQueueCapacity
            && ConcurrentSessions == OpenLoopExecutionOptions.DefaultConcurrentSessions
            && MaximumNodes == OpenLoopExecutionOptions.DefaultMaximumNodes
            && OperationDeadlineMilliseconds == OpenLoopExecutionOptions.DefaultOperationDeadlineMilliseconds
            && DrainMilliseconds == OpenLoopExecutionOptions.DefaultDrainMilliseconds
            && ControlPollMilliseconds == OpenLoopExecutionOptions.DefaultControlPollMilliseconds
            && SpinWindowMicroseconds == OpenLoopExecutionOptions.DefaultSpinWindowMicroseconds;

    internal long OperationDeadlineTicks(TimeProvider timeProvider)
        => ToTimestampTicks(OperationDeadlineMilliseconds, MillisecondsPerSecond, timeProvider: timeProvider);

    internal long DrainTicks(TimeProvider timeProvider)
        => ToTimestampTicks(DrainMilliseconds, MillisecondsPerSecond, timeProvider: timeProvider);

    internal long SpinWindowTicks(TimeProvider timeProvider)
        => ToTimestampTicks(SpinWindowMicroseconds, MicrosecondsPerSecond, timeProvider: timeProvider);

    private static long ToTimestampTicks(int amount, int unitsPerSecond, TimeProvider timeProvider)
    {
        var numerator = checked((long)amount * timeProvider.TimestampFrequency);
        return checked((numerator + unitsPerSecond - CeilingRoundingAdjustment) / unitsPerSecond);
    }
}
