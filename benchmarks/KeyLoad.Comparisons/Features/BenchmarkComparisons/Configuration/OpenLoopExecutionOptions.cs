namespace KeyLoad.Comparisons;

/// <summary>Canonical centrally bound operational limits for the qualified open-loop cohort.</summary>
[ConfigurationOptions]
public sealed class OpenLoopExecutionOptions
{
    /// <summary>The one native comparison-host section for open-loop execution policy.</summary>
    public const string SectionName = "Benchmarks:OpenLoopExecution";

    internal const int DefaultQueueCapacity = 64;
    internal const int DefaultConcurrentSessions = 16;
    internal const int DefaultMaximumNodes = 3;
    internal const int DefaultOperationDeadlineMilliseconds = 30_000;
    internal const int DefaultDrainMilliseconds = 30_000;
    internal const int DefaultControlPollMilliseconds = 100;
    internal const int DefaultSpinWindowMicroseconds = 200;

    /// <summary>Gets or sets the maximum immediately queued operations.</summary>
    public int QueueCapacity { get; set; } = DefaultQueueCapacity;
    /// <summary>Gets or sets the maximum concurrently open native sessions.</summary>
    public int ConcurrentSessions { get; set; } = DefaultConcurrentSessions;
    /// <summary>Gets or sets the maximum admitted native cluster size.</summary>
    public int MaximumNodes { get; set; } = DefaultMaximumNodes;
    /// <summary>Gets or sets each operation's scheduled-arrival deadline in milliseconds.</summary>
    public int OperationDeadlineMilliseconds { get; set; } = DefaultOperationDeadlineMilliseconds;
    /// <summary>Gets or sets the final native work drain in milliseconds.</summary>
    public int DrainMilliseconds { get; set; } = DefaultDrainMilliseconds;
    /// <summary>Gets or sets the child cancellation-control polling interval in milliseconds.</summary>
    public int ControlPollMilliseconds { get; set; } = DefaultControlPollMilliseconds;
    /// <summary>Gets or sets the timeline's bounded final spin window in microseconds.</summary>
    public int SpinWindowMicroseconds { get; set; } = DefaultSpinWindowMicroseconds;

    /// <summary>Copies one already validated native options value into immutable evidence policy.</summary>
    /// <param name="options">The exact options snapshot resolved by native IOptions.</param>
    /// <returns>The immutable execution-policy snapshot.</returns>
    internal static OpenLoopExecutionPolicy Snapshot(OpenLoopExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(options.QueueCapacity, options.ConcurrentSessions, options.MaximumNodes,
            options.OperationDeadlineMilliseconds, options.DrainMilliseconds,
            options.ControlPollMilliseconds, options.SpinWindowMicroseconds);
    }
}
