
namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Central native database memory and WAL deployment policy for comparison cells.</summary>
[ConfigurationOptions]
internal sealed record BenchmarkDeploymentOptions
{
    private const int DefaultNeo4jInitialHeap = 256;
    private const int DefaultNeo4jMaximumHeap = 512;
    private const int DefaultNeo4jPageCache = 256;
    private const int DefaultOpenSearchHeap = 512;
    private const int DefaultPostgresSlotRetention = 512;
    private const int DefaultDocumentPostgresConnections = 512;
    private const int MinimumDocumentPostgresConnections = 512;
    private const int MaximumDocumentPostgresConnections = 4096;
    private const int DocumentRequestsPerScope = 512;
    private const int DocumentMaximumRecords = 1_100_000;
    private const long DocumentMaximumReadBytes = 2_147_483_648;
    private const int DocumentSnapshotThreshold = 2_000_000;
    private const int MinimumPositive = 1;
    private const int MaximumMemoryMegabytes = 16384;
    private const int MaximumWalMegabytes = 65536;
    internal const string SectionName = "Benchmarks:Deployment";
    internal const string ValidationMessage = "Comparison deployment memory and WAL settings are invalid.";
    public int Neo4jInitialHeapMegabytes { get; init; } = DefaultNeo4jInitialHeap;
    public int Neo4jMaximumHeapMegabytes { get; init; } = DefaultNeo4jMaximumHeap;
    public int Neo4jPageCacheMegabytes { get; init; } = DefaultNeo4jPageCache;
    public int OpenSearchHeapMegabytes { get; init; } = DefaultOpenSearchHeap;
    public int PostgresSlotWalRetentionMegabytes { get; init; } = DefaultPostgresSlotRetention;
    public int DocumentKeyLoadRequestsPerScope { get; init; } = DocumentRequestsPerScope;
    public int DocumentPostgresMaximumConnections { get; init; } = DefaultDocumentPostgresConnections;
    public int DocumentKeyLoadMaximumScanRecords { get; init; } = DocumentMaximumRecords;
    public long DocumentKeyLoadMaximumQueryReadBytes { get; init; } = DocumentMaximumReadBytes;
    public int DocumentKeyLoadSnapshotThreshold { get; init; } = DocumentSnapshotThreshold;
    internal bool IsValid() => DocumentKeyLoadRequestsPerScope == DocumentRequestsPerScope
        && DocumentPostgresMaximumConnections is >= MinimumDocumentPostgresConnections and <= MaximumDocumentPostgresConnections
        && DocumentKeyLoadMaximumScanRecords == DocumentMaximumRecords
        && DocumentKeyLoadMaximumQueryReadBytes == DocumentMaximumReadBytes
        && DocumentKeyLoadSnapshotThreshold == DocumentSnapshotThreshold
        && ValidMemory(Neo4jInitialHeapMegabytes) && ValidMemory(Neo4jMaximumHeapMegabytes)
        && Neo4jInitialHeapMegabytes <= Neo4jMaximumHeapMegabytes && ValidMemory(Neo4jPageCacheMegabytes)
        && ValidMemory(OpenSearchHeapMegabytes) && PostgresSlotWalRetentionMegabytes is >= MinimumPositive and <= MaximumWalMegabytes;
    private static bool ValidMemory(int value) => value is >= MinimumPositive and <= MaximumMemoryMegabytes;
}
