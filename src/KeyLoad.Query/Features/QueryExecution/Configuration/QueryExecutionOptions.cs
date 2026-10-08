namespace KeyLoad.Query;

/// <summary>Centrally configured lifetime of query continuation authority.</summary>
[ConfigurationOptions]
public sealed record QueryExecutionOptions
{
    /// <summary>The section bound by the server composition root.</summary>
    public const string SectionName = "KeyLoad:QueryExecution";

    /// <summary>Safe startup failure for an invalid query continuation lifetime.</summary>
    public const string ValidationMessage = "The query continuation, parsing and search execution settings are invalid.";

    private const int DefaultCursorLifetimeMinutes = 5;
    private const int DefaultMaximumProjection = 256;
    private const int DefaultMaximumOrdering = 16;
    private const int DefaultMaximumParameters = 256;
    private const int DefaultMaximumInValues = 256;
    private const int DefaultMaximumPartitions = 8;
    private const int DefaultMaximumConcurrentPartitionLeaves = 2;
    private const int DefaultMaximumSearchResults = 1_000;
    private const int DefaultMaximumSearchTextBytes = 4_096;
    private const int DefaultTextBudgetCheckInterval = 512;
    private const int DefaultMaximumDocumentWords = 65_536;
    private const int DefaultMaximumWordCharacters = 4_096;
    private const int DefaultSqlBudgetCheckInterval = 256;
    private const int MaximumConformingSearchResults = 1_000;
    private const int MaximumConformingSearchTextBytes = 4_096;
    private const int MinimumResultByteCount = 1;
    private const int MinimumWorkCount = 1;
    private const int MaximumConformingPartitions = 8;
    private const int MaximumCursorLifetimeDays = 365;
    private static readonly TimeSpan MaximumCursorLifetime = TimeSpan.FromDays(MaximumCursorLifetimeDays);

    /// <summary>Explicitly enables the qualified provisioned vector-only native ANN read surface.</summary>
    public bool EnableApproximateSearch { get; init; }

    /// <summary>Gets an additional complete query/read result byte ceiling; null retains the native batch ceiling.</summary>
    public int? MaximumResultBytes { get; init; }

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

    /// <summary>Gets the downward concurrency ceiling for independently admitted physical-owner leaves.</summary>
    public int MaximumConcurrentPartitionLeaves { get; init; } = DefaultMaximumConcurrentPartitionLeaves;

    /// <summary>Gets the maximum ranked search results within the existing search contract.</summary>
    public int MaximumSearchResults { get; init; } = DefaultMaximumSearchResults;
    /// <summary>Gets the maximum UTF8 bytes in one text query.</summary>
    public int MaximumSearchTextBytes { get; init; } = DefaultMaximumSearchTextBytes;
    /// <summary>Gets the rune cadence for checking the shared text execution budget.</summary>
    public int TextBudgetCheckInterval { get; init; } = DefaultTextBudgetCheckInterval;
    /// <summary>Gets the maximum tokenization work in one document.</summary>
    public int MaximumDocumentWords { get; init; } = DefaultMaximumDocumentWords;
    /// <summary>Gets the maximum characters retained for one normalized text token.</summary>
    public int MaximumWordCharacters { get; init; } = DefaultMaximumWordCharacters;
    /// <summary>Gets the character cadence for checking long SQL token work.</summary>
    public int SqlBudgetCheckInterval { get; init; } = DefaultSqlBudgetCheckInterval;

    /// <summary>Checks that the configured duration remains positive and bounded.</summary>
    /// <returns>Whether the configured lifetime is valid.</returns>
    public bool IsValid() => (MaximumResultBytes is null or >= MinimumResultByteCount) && CursorLifetime > TimeSpan.Zero && CursorLifetime <= MaximumCursorLifetime
        && MaximumProjection >= MinimumWorkCount && MaximumOrdering >= MinimumWorkCount
        && MaximumParameters is >= MinimumWorkCount and <= DefaultMaximumParameters && MaximumInValues >= MinimumWorkCount
        && MaximumPartitions >= MinimumWorkCount && MaximumPartitions <= MaximumConformingPartitions
        && MaximumConcurrentPartitionLeaves is >= MinimumWorkCount and <= MaximumConformingPartitions
        && MaximumSearchResults >= MinimumWorkCount && MaximumSearchResults <= MaximumConformingSearchResults
        && MaximumSearchTextBytes >= MinimumWorkCount && MaximumSearchTextBytes <= MaximumConformingSearchTextBytes
        && TextBudgetCheckInterval >= MinimumWorkCount && MaximumDocumentWords >= MinimumWorkCount
        && MaximumWordCharacters >= MinimumWorkCount && SqlBudgetCheckInterval is >= MinimumWorkCount and <= DefaultSqlBudgetCheckInterval;

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
