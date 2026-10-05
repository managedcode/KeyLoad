namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Centrally validated operational limits for the private request lifecycle probe.</summary>
[ConfigurationOptions]
internal sealed class RequestProbeExecutionOptions
{
    internal const string SectionName = "KeyLoad:RequestProbeExecution";
    internal const string ValidationMessage = "Private request probe durations and active gates exceed their accepted bounds.";
    private const int MaximumHoldSeconds = 60;
    private const int DefaultPollMilliseconds = 100;
    private const int MaximumPollSeconds = 1;
    private const int MaximumPermittedGates = 4;
    private const int MinimumPositiveCount = 1;

    /// <summary>Gets or sets the finite cancellation ceiling for one private probe hold.</summary>
    public TimeSpan HoldTimeout { get; set; } = TimeSpan.FromSeconds(MaximumHoldSeconds);
    /// <summary>Gets or sets the interval between private arm release checks.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(DefaultPollMilliseconds);
    /// <summary>Gets or sets the maximum simultaneously held private probe gates.</summary>
    public int MaximumActiveGates { get; set; } = MaximumPermittedGates;

    internal bool IsValid() => HoldTimeout > TimeSpan.Zero && HoldTimeout <= TimeSpan.FromSeconds(MaximumHoldSeconds)
        && PollInterval > TimeSpan.Zero && PollInterval <= TimeSpan.FromSeconds(MaximumPollSeconds)
        && PollInterval < HoldTimeout
        && MaximumActiveGates is >= MinimumPositiveCount and <= MaximumPermittedGates;
}
