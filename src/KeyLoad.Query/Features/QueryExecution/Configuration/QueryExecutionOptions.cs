namespace KeyLoad.Query;

/// <summary>Centrally configured lifetime of query continuation authority.</summary>
[ConfigurationOptions]
public sealed record QueryExecutionOptions
{
    /// <summary>The section bound by the server composition root.</summary>
    public const string SectionName = "KeyLoad:QueryExecution";

    /// <summary>Safe startup failure for an invalid query continuation lifetime.</summary>
    public const string ValidationMessage = "The query cursor lifetime must be positive and at most one year.";

    private const int DefaultCursorLifetimeMinutes = 5;
    private const int MaximumCursorLifetimeDays = 365;
    private static readonly TimeSpan MaximumCursorLifetime = TimeSpan.FromDays(MaximumCursorLifetimeDays);

    /// <summary>Gets the maximum age of a newly issued authorized query continuation.</summary>
    public TimeSpan CursorLifetime { get; init; } = TimeSpan.FromMinutes(DefaultCursorLifetimeMinutes);

    /// <summary>Checks that the configured duration remains positive and bounded.</summary>
    /// <returns>Whether the configured lifetime is valid.</returns>
    public bool IsValid() => CursorLifetime > TimeSpan.Zero && CursorLifetime <= MaximumCursorLifetime;

    /// <summary>Rejects invalid settings before query execution is admitted.</summary>
    /// <exception cref="InvalidOperationException">The configured continuation lifetime is invalid.</exception>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
