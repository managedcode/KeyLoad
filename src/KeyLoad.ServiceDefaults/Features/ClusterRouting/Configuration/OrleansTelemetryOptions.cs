namespace KeyLoad.ServiceDefaults.Features.ClusterRouting.Configuration;

/// <summary>Validated bounds for privacy-safe native Orleans telemetry processing.</summary>
[KeyLoad.ConfigurationOptions]
public sealed class OrleansTelemetryOptions
{
    /// <summary>The typed configuration section for Orleans telemetry limits.</summary>
    public const string SectionName = "KeyLoad:OrleansTelemetry";

    /// <summary>The validation error for unsafe telemetry-processing limits.</summary>
    public const string ValidationMessage = "Orleans telemetry bounds must remain within the reviewed privacy limits.";

    private const int DefaultMaximumTags = 32;
    private const int DefaultMaximumEvents = 24;
    private const int DefaultMaximumBaggageItems = 8;
    private const int DefaultMaximumTagValueCharacters = 256;
    private const int MinimumMaximumTags = 1;
    private const int MinimumMaximumEvents = 1;
    private const int MinimumMaximumBaggageItems = 0;
    private const int AbsoluteMaximumTags = 32;
    private const int AbsoluteMaximumEvents = 24;
    private const int AbsoluteMaximumBaggageItems = 8;
    private const int MinimumMaximumTagValueCharacters = 1;
    private const int AbsoluteMaximumTagValueCharacters = 256;

    /// <summary>Gets or sets the maximum tags inspected on one activity.</summary>
    public int MaximumTags { get; set; } = DefaultMaximumTags;

    /// <summary>Gets or sets the maximum events inspected on one activity.</summary>
    public int MaximumEvents { get; set; } = DefaultMaximumEvents;

    /// <summary>Gets or sets the maximum baggage keys removed from one activity at start.</summary>
    public int MaximumBaggageItems { get; set; } = DefaultMaximumBaggageItems;

    /// <summary>Gets or sets the maximum text length inspected before tag normalization.</summary>
    public int MaximumTagValueCharacters { get; set; } = DefaultMaximumTagValueCharacters;

    /// <summary>Checks that all privacy budgets are bounded by the reviewed contract.</summary>
    /// <returns>Whether every configured limit is within its safe range.</returns>
    public bool IsValid()
        => MaximumTags >= MinimumMaximumTags && MaximumTags <= AbsoluteMaximumTags
            && MaximumEvents >= MinimumMaximumEvents && MaximumEvents <= AbsoluteMaximumEvents
            && MaximumBaggageItems >= MinimumMaximumBaggageItems
            && MaximumBaggageItems <= AbsoluteMaximumBaggageItems
            && MaximumTagValueCharacters >= MinimumMaximumTagValueCharacters
            && MaximumTagValueCharacters <= AbsoluteMaximumTagValueCharacters;
}
