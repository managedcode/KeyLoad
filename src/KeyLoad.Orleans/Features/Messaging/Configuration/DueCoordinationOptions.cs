namespace KeyLoad.Orleans;

/// <summary>Centrally configured deadlines and discovery cadence for per-silo due coordination.</summary>
[ConfigurationOptions]
public sealed class DueCoordinationOptions
{
    /// <summary>The configuration section bound by the server composition root.</summary>
    public const string SectionName = "KeyLoad:DueCoordination";

    /// <summary>The startup failure reported for invalid due scheduling configuration.</summary>
    public const string ValidationMessage = "Due scheduling durations must be positive and at most one minute; polling must be at most one second, cycle cadence at least 500 milliseconds, and uncertainty retries between zero and one.";

    /// <summary>The configured retry count which disables uncertainty retries.</summary>
    public const int NoUncertaintyRetries = 0;

    private const int DefaultDispatchDeadlineSeconds = 5;
    private const int DefaultPollIntervalSeconds = 1;
    private const int DefaultMinimumCycleCadenceMilliseconds = 500;
    private const int MaximumDurationMinutes = 1;
    private const int MaximumUncertaintyRetries = 1;
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(MaximumDurationMinutes);
    private static readonly TimeSpan MaximumPollInterval = TimeSpan.FromSeconds(DefaultPollIntervalSeconds);
    private static readonly TimeSpan MinimumPermittedCycleCadence =
        TimeSpan.FromMilliseconds(DefaultMinimumCycleCadenceMilliseconds);

    /// <summary>Gets or sets the cancellation deadline shared by service and coordinator dispatch.</summary>
    public TimeSpan DispatchDeadline { get; set; } = TimeSpan.FromSeconds(DefaultDispatchDeadlineSeconds);

    /// <summary>Gets or sets the fallback polling interval when no applied-position change arrives.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(DefaultPollIntervalSeconds);

    /// <summary>Gets or sets the minimum interval between consecutive discovery cycle starts.</summary>
    public TimeSpan MinimumCycleCadence { get; set; } =
        TimeSpan.FromMilliseconds(DefaultMinimumCycleCadenceMilliseconds);

    /// <summary>Gets or sets the bounded retry count for an uncertain outcome, preserving command identity.</summary>
    public int UncertaintyRetryCount { get; set; } = MaximumUncertaintyRetries;

    /// <summary>Checks that configured scheduling durations are positive and no greater than one minute.</summary>
    /// <returns>Whether all durations are within the accepted startup bounds.</returns>
    public bool IsValid() => IsBounded(DispatchDeadline) && IsBounded(PollInterval) && IsBounded(MinimumCycleCadence)
        && PollInterval <= MaximumPollInterval && MinimumCycleCadence >= MinimumPermittedCycleCadence
        && UncertaintyRetryCount is >= NoUncertaintyRetries and <= MaximumUncertaintyRetries;

    private static bool IsBounded(TimeSpan duration) => duration > TimeSpan.Zero && duration <= MaximumDuration;
}
