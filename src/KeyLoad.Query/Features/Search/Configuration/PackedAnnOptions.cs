namespace KeyLoad.Query.Features.Search;

/// <summary>Central resource and construction settings for the disposable packed ANN index.</summary>
[ConfigurationOptions]
public sealed record PackedAnnOptions
{
    /// <summary>The centrally bound ANN scenario section.</summary>
    public const string SectionName = "KeyLoad:PackedAnn";
    /// <summary>The rejection detail for invalid ANN settings.</summary>
    public const string ValidationMessage = "The packed ANN options are invalid.";
    private const int MinimumConnections = 4;
    private const int ConnectionsEight = 8;
    private const int DefaultConnections = 16;
    private const int ConnectionsThirtyTwo = 32;
    private const int MaximumConnections = 64;
    private const int DefaultEfConstruction = 128;
    private const int DefaultEfSearch = 128;
    private const int DefaultMaxLevel = 16;
    private const int DefaultExactThreshold = 256;
    private const int DefaultMaxRecords = 5_000_000;
    private const long DefaultMaxIndexBytes = 268_435_456;
    private const long DefaultMaxScratchBytes = 8_388_608;
    private const ulong DefaultSeed = 0x4B45594C4F414431UL;
    private const int DefaultVectorBlockBytes = 65_536;
    private const int DefaultMaximumSearchBreadth = 4_096;
    private const int DefaultMaximumSearchResults = 1_000;
    private const int DefaultVectorBudgetCheckInterval = 256;
    private const int MinimumPositiveLimit = 1;
    private const int MinimumExactThreshold = 0;
    private const int MinimumMemoryBytes = 1_024;
    private const long MaximumIndexBytes = 8_589_934_592;
    private const long MaximumScratchBytes = 268_435_456;

    /// <summary>Maximum graph neighbors per level.</summary>
    public int Connections { get; init; } = DefaultConnections;
    /// <summary>Construction candidate breadth.</summary>
    public int EfConstruction { get; init; } = DefaultEfConstruction;
    /// <summary>Initial search candidate breadth.</summary>
    public int EfSearch { get; init; } = DefaultEfSearch;
    /// <summary>Maximum deterministic graph level.</summary>
    public int MaxLevel { get; init; } = DefaultMaxLevel;
    /// <summary>Eligible populations at or below this size use exact search.</summary>
    public int ExactThreshold { get; init; } = DefaultExactThreshold;
    /// <summary>Maximum source record count.</summary>
    public int MaxRecords { get; init; } = DefaultMaxRecords;
    /// <summary>Maximum retained modeled index bytes.</summary>
    public long MaxIndexBytes { get; init; } = DefaultMaxIndexBytes;
    /// <summary>Maximum modeled construction or search scratch bytes.</summary>
    public long MaxScratchBytes { get; init; } = DefaultMaxScratchBytes;
    /// <summary>Deterministic level-generation seed.</summary>
    public ulong Seed { get; init; } = DefaultSeed;
    /// <summary>Target packed vector allocation block size.</summary>
    public int VectorBlockBytes { get; init; } = DefaultVectorBlockBytes;
    /// <summary>Maximum expanded candidate search breadth.</summary>
    public int MaximumSearchBreadth { get; init; } = DefaultMaximumSearchBreadth;
    /// <summary>Maximum accepted result count.</summary>
    public int MaximumSearchResults { get; init; } = DefaultMaximumSearchResults;

    /// <summary>Component cadence for cancellation and execution-budget checks during copying.</summary>
    public int VectorBudgetCheckInterval { get; init; } = DefaultVectorBudgetCheckInterval;

    /// <summary>Whether settings preserve the current bounded construction contract.</summary>
    public bool IsValid() => Connections is MinimumConnections or ConnectionsEight or DefaultConnections or ConnectionsThirtyTwo or MaximumConnections
        && EfConstruction >= Connections && EfConstruction <= DefaultMaximumSearchBreadth
        && EfSearch is >= MinimumPositiveLimit and <= DefaultMaximumSearchBreadth
        && MaxLevel is >= MinimumPositiveLimit and <= DefaultMaxLevel
        && ExactThreshold is >= MinimumExactThreshold and <= DefaultMaximumSearchBreadth
        && MaxRecords is >= MinimumPositiveLimit and <= DefaultMaxRecords
        && MaxIndexBytes is >= MinimumMemoryBytes and <= MaximumIndexBytes
        && MaxScratchBytes is >= MinimumMemoryBytes and <= MaximumScratchBytes
        && VectorBlockBytes is >= MinimumPositiveLimit and <= DefaultVectorBlockBytes
        && MaximumSearchBreadth is >= MinimumPositiveLimit and <= DefaultMaximumSearchBreadth
        && MaximumSearchResults is >= MinimumPositiveLimit and <= DefaultMaximumSearchResults
        && VectorBudgetCheckInterval is >= MinimumPositiveLimit and <= DefaultVectorBudgetCheckInterval;

    /// <summary>Rejects invalid settings using the existing ANN domain validation error.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw Errors.Fail(ErrorCode.Validation, ValidationMessage);
        }
    }
}
