using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>Session resource bootstrap is independent from its session-v1 measurement identity.</summary>
public static class BenchmarkSessionContract
{
    /// <summary>The current source-bound session evidence profile.</summary>
    public const string Profile = "session-v1";
    private const string KeyLoad = "KeyLoad", PostgreSql = "PostgreSQL + pgvector", Qdrant = "Qdrant";
    private const string RabbitMq = "RabbitMQ", Redis = "Redis", Neo4j = "Neo4j", Mongo = "MongoDB";
    private const string OpenSearch = "OpenSearch", Kurrent = "KurrentDB", Surreal = "SurrealDB", Helix = "HelixDB";
    private const string Neo4jUnavailable = "Neo4j Community does not provide native clustering; Enterprise licensing is excluded.";
    private const string SurrealUnavailable = "SurrealDB Community's experimental TiKV clustering is outside the qualified native topology; only the persistent standalone server is measured.";
    private const string HelixUnavailable = "The self-hosted HelixDB server is standalone; managed cloud high availability is outside this native comparison.";
    private const string OverrideDocuments = "Documents";
    private const string OverrideOperations = "Operations";
    private const string OverrideWarmup = "Warmup";
    private const string OverrideRepetitions = "Repetitions";
    private const string OverrideConcurrency = "Concurrency";
    private const string OverridePayloadBytes = "PayloadBytes";
    private const string OverrideSeed = "Seed";
    private const string OverrideDimensions = "Dimensions";
    private const string OverrideTopK = "TopK";
    private const string OverrideTimeoutSeconds = "TimeoutSeconds";
    private const string OverrideScaleProfile = "ScaleProfile";
    private const string OverrideVectorProfile = "VectorProfile";
    private const string OverrideOpenLoopRate = "OpenLoopRate";
    private const string OverrideDocumentScenario = "DocumentScenario";
    private const string OverrideDocumentRecords = "DocumentRecords";
    private const string OverrideDocumentClients = "DocumentClients";
    private const string OverrideGraphVertices = "GraphVertices";
    private const string OverrideGraphFanOut = "GraphFanOut";
    private const string OverrideGraphDepth = "GraphDepth";
    private const string OverrideScenario = "Scenario";
    private const string OverrideProfile = "Profile";
    /// <summary>Superseded per-cell knobs rejected before any native resource allocation.</summary>
    public static ImmutableArray<string> ForbiddenOverrides
    {
        get;
    } =
    [
        OverrideDocuments, OverrideOperations, OverrideWarmup, OverrideRepetitions, OverrideConcurrency, OverridePayloadBytes, OverrideSeed, OverrideDimensions, OverrideTopK, OverrideTimeoutSeconds, OverrideScaleProfile, OverrideVectorProfile, OverrideOpenLoopRate, OverrideDocumentScenario, OverrideDocumentRecords, OverrideDocumentClients, OverrideGraphVertices, OverrideGraphFanOut, OverrideGraphDepth, OverrideScenario, OverrideProfile
    ];
    /// <summary>The exact databases participating in the session cohort.</summary>
    public static ImmutableArray<string> Targets { get; } = [KeyLoad, PostgreSql, Qdrant, RabbitMq, Redis, Neo4j, Mongo, OpenSearch, Kurrent, Surreal, Helix];
    /// <summary>Native deployment limitations; an operational failure never becomes an unavailable topology.</summary>
    public static ImmutableArray<BenchmarkSessionUnsupportedTopology> UnsupportedTopologies
    {
        get;
    } =
    [
        new(Neo4j, [BenchmarkSessionLimits.ReplicatedNodeCount], Neo4jUnavailable),
        new(Surreal, [BenchmarkSessionLimits.ReplicatedNodeCount], SurrealUnavailable),
        new(Helix, [BenchmarkSessionLimits.ReplicatedNodeCount], HelixUnavailable)
    ];
    /// <summary>Creates the existing native document topology selection without initializing or loading its data.</summary>
    public static BenchmarkSessionSelection ResourceSelection(string target, int nodeCount)
    {
        var selection = new BenchmarkSessionSelection(target, nodeCount);
        selection.Validate();
        return selection;
    }
}
