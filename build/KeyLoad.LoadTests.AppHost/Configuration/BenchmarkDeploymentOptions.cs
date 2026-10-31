
namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

/// <summary>Central execution database memory and WAL deployment policy for comparison cells.</summary>
[ConfigurationOptions]
internal sealed record BenchmarkDeploymentOptions
{
    private const string DefaultKeyLoadLogLevel = "Warning";
    private const int DefaultNeo4jInitialHeap = 256;
    private const int DefaultNeo4jMaximumHeap = 512;
    private const int DefaultNeo4jPageCache = 256;
    private const int DefaultOpenSearchHeap = 1024;
    private const int DefaultInfluxQueryMemoryPercent = 70;
    private const int DefaultInfluxWalFilesPerSnapshot = 1;
    private const int MaximumInfluxWalFilesPerSnapshot = 600;
    private const int MaximumQueryMemoryPercent = 90;
    private const int DefaultPostgresSlotRetention = 512;
    private const int DefaultDocumentPostgresConnections = 512;
    private const int MinimumDocumentPostgresConnections = 512;
    private const int MaximumDocumentPostgresConnections = 4096;
    private const int MaximumMemoryMegabytes = 16384;
    private const int MaximumWalMegabytes = 65536;
    internal const string SectionName = "Benchmarks:Deployment";
    internal const string ValidationMessage = "Comparison deployment memory and WAL settings are invalid.";
    public int Neo4jInitialHeapMegabytes { get; init; } = DefaultNeo4jInitialHeap;
    public int Neo4jMaximumHeapMegabytes { get; init; } = DefaultNeo4jMaximumHeap;
    public int Neo4jPageCacheMegabytes { get; init; } = DefaultNeo4jPageCache;
    public int OpenSearchHeapMegabytes { get; init; } = DefaultOpenSearchHeap;
    public int InfluxQueryMemoryPercent { get; init; } = DefaultInfluxQueryMemoryPercent;
    public int InfluxWalFilesPerSnapshot { get; init; } = DefaultInfluxWalFilesPerSnapshot;
    public int PostgresSlotWalRetentionMegabytes { get; init; } = DefaultPostgresSlotRetention;
    public int DocumentPostgresMaximumConnections { get; init; } = DefaultDocumentPostgresConnections;
    public string KeyLoadLogLevel { get; init; } = DefaultKeyLoadLogLevel;
    internal bool IsValid() => DocumentPostgresMaximumConnections is >= MinimumDocumentPostgresConnections and <= MaximumDocumentPostgresConnections
        && ValidMemory(Neo4jInitialHeapMegabytes) && ValidMemory(Neo4jMaximumHeapMegabytes)
        && Neo4jInitialHeapMegabytes <= Neo4jMaximumHeapMegabytes && ValidMemory(Neo4jPageCacheMegabytes)
        && ValidMemory(OpenSearchHeapMegabytes) && InfluxQueryMemoryPercent is >= 1 and <= MaximumQueryMemoryPercent
        && InfluxWalFilesPerSnapshot is >= 1 and <= MaximumInfluxWalFilesPerSnapshot
        && PostgresSlotWalRetentionMegabytes is >= 1 and <= MaximumWalMegabytes
        && !string.IsNullOrWhiteSpace(KeyLoadLogLevel);
    private static bool ValidMemory(int value) => value is >= 1 and <= MaximumMemoryMegabytes;
}
