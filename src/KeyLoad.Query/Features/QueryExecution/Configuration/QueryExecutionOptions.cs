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
    private const int DefaultMaximumProjection = 256;
    private const int DefaultMaximumOrdering = 16;
    private const int DefaultMaximumParameters = 256;
    private const int DefaultMaximumInValues = 256;
    private const int DefaultMaximumPartitions = 8;
    private const int MinimumWorkCount = 1;
    private const int MaximumConformingPartitions = 8;
    private const int MaximumCursorLifetimeDays = 365;
    private static readonly TimeSpan MaximumCursorLifetime = TimeSpan.FromDays(MaximumCursorLifetimeDays);

    /// <summary>Gets the maximum age of a newly issued authorized query continuation.</summary>
    public TimeSpan CursorLifetime { get; init; } = TimeSpan.FromMinutes(DefaultCursorLifetimeMinutes);

    /// <summary>Gets the maximum selected fields in one query.</summary>
    public int MaximumProjection { get; init; } = DefaultMaximumProjection;
    /// <summary>Gets the maximum ordering fields in one query.</summary>
    public int MaximumOrdering { get; init; } = DefaultMaximumOrdering;
    /// <summary>Gets the maximum named SQL or AST parameters.</summary>
    public int MaximumParameters { get; init; } = DefaultMaximumParameters;
    /// <summary>Gets the maximum operands in one IN predicate.</summary>
    public int MaximumInValues { get; init; } = DefaultMaximumInValues;
    /// <summary>Gets the maximum leaves admitted to one partition query plan.</summary>
    public int MaximumPartitions { get; init; } = DefaultMaximumPartitions;

    /// <summary>Checks that the configured duration remains positive and bounded.</summary>
    /// <returns>Whether the configured lifetime is valid.</returns>
    public bool IsValid() => CursorLifetime > TimeSpan.Zero && CursorLifetime <= MaximumCursorLifetime
        && MaximumProjection >= MinimumWorkCount && MaximumOrdering >= MinimumWorkCount
        && MaximumParameters >= MinimumWorkCount && MaximumInValues >= MinimumWorkCount
        && MaximumPartitions >= MinimumWorkCount && MaximumPartitions <= MaximumConformingPartitions;

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
