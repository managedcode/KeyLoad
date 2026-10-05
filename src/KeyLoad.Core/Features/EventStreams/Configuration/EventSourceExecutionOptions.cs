namespace KeyLoad.Core;

/// <summary>Centrally configured lifetime of signed event-source continuation authority.</summary>
[ConfigurationOptions]
public sealed record EventSourceExecutionOptions
{
    /// <summary>The section bound by the server composition root.</summary>
    public const string SectionName = "KeyLoad:EventSourceExecution";
    /// <summary>Safe startup rejection for an invalid event continuation lifetime.</summary>
    public const string ValidationMessage = "The event source cursor lifetime must be positive and at most one year.";
    private const int DefaultCursorLifetimeHours = 24;
    private const int MaximumCursorLifetimeDays = 365;
    private static readonly TimeSpan MaximumCursorLifetime = TimeSpan.FromDays(MaximumCursorLifetimeDays);

    /// <summary>Gets the maximum age of a newly issued signed event continuation.</summary>
    public TimeSpan CursorLifetime { get; init; } = TimeSpan.FromHours(DefaultCursorLifetimeHours);

    /// <summary>Checks that the continuation lifetime remains positive and bounded.</summary>
    /// <returns>Whether the configured event cursor lifetime is valid.</returns>
    public bool IsValid() => CursorLifetime > TimeSpan.Zero && CursorLifetime <= MaximumCursorLifetime;

    /// <summary>Rejects invalid settings before event reads are admitted.</summary>
    /// <exception cref="InvalidOperationException">The configured continuation lifetime is invalid.</exception>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
