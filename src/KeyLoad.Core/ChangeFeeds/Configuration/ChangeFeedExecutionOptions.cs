namespace KeyLoad.Core;

/// <summary>Centrally configured ChangeFeeds execution and admission policy.</summary>
[ConfigurationOptions]
public sealed record ChangeFeedExecutionOptions
{
    /// <summary>The centrally bound scenario section.</summary>
    public const string SectionName = "KeyLoad:ChangeFeedExecution";
    /// <summary>The startup rejection detail for invalid scenario settings.</summary>
    public const string ValidationMessage = "The ChangeFeeds execution settings are invalid.";
    private const int DefaultCursorLifetimeHours = 24;
    private const int DefaultProjectionBatchLifetimeMinutes = 5;
    private const int MaximumLifetimeDays = 365;
    private const int DefaultMaximumProjectionResources = 256;
    private const int DefaultMaximumProjectionMutationKinds = 32;
    private const int MinimumWorkCount = 1;
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(MaximumLifetimeDays);

    /// <summary>Maximum age of a newly issued signed change cursor.</summary>
    public TimeSpan CursorLifetime { get; init; } = TimeSpan.FromHours(DefaultCursorLifetimeHours);
    /// <summary>Maximum age of a newly issued signed projection batch token.</summary>
    public TimeSpan ProjectionBatchLifetime { get; init; } = TimeSpan.FromMinutes(DefaultProjectionBatchLifetimeMinutes);
    /// <summary>Maximum resource filters in one projection definition.</summary>
    public int MaximumProjectionResources { get; init; } = DefaultMaximumProjectionResources;
    /// <summary>Maximum mutation-kind filters in one projection definition.</summary>
    public int MaximumProjectionMutationKinds { get; init; } = DefaultMaximumProjectionMutationKinds;

    /// <summary>Whether durations and projection work remain positive and bounded.</summary>
    public bool IsValid() => CursorLifetime > TimeSpan.Zero && CursorLifetime <= MaximumLifetime
        && ProjectionBatchLifetime > TimeSpan.Zero && ProjectionBatchLifetime <= MaximumLifetime
        && MaximumProjectionResources is >= MinimumWorkCount and <= DefaultMaximumProjectionResources
        && MaximumProjectionMutationKinds is >= MinimumWorkCount and <= DefaultMaximumProjectionMutationKinds;

    /// <summary>Rejects invalid settings before operation admission.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
