using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.Comparisons;

/// <summary>Identifies a workload operation measured by the comparison harness.</summary>
public enum Scenario
{
    /// <summary>Reads one previously initialized document by its identifier.</summary>
    PointRead,
    /// <summary>Writes a generated document.</summary>
    DocumentWrite,
    /// <summary>Finds the exact top matching vectors in the initialized corpus.</summary>
    VectorExact,
    /// <summary>Enqueues, receives, and acknowledges one queue message.</summary>
    QueueCycle,
    /// <summary>Reads the exact outgoing-neighbor documents for a graph vertex.</summary>
    GraphNeighbors,
    /// <summary>Traverses the generated graph from a starting vertex.</summary>
    GraphTraverse,
    /// <summary>Appends a generated event to a document stream.</summary>
    StreamAppend,
    /// <summary>Reads an event from a document stream.</summary>
    StreamRead
}

/// <summary>Describes the deployment topology requested for a comparison target.</summary>
[TypeConverter(typeof(ComparisonTopologyConverter))]
public enum ComparisonTopology
{
    /// <summary>Requests a standalone external-engine profile; the KeyLoad target retains its required RF3 topology.</summary>
    [JsonStringEnumMemberName(ComparisonTopologyNames.Single)]
    Standalone,
    /// <summary>Uses a replicated target topology and records its cluster evidence.</summary>
    Replicated
}

/// <summary>Configures the generated workload and its measurement budgets.</summary>
public sealed record ComparisonOptions
{
    private const string ConfigurationSection = "Benchmarks";
    /// <summary>Gets or initializes the requested target topology.</summary>
    public ComparisonTopology Topology { get; init; } = ComparisonTopology.Standalone;
    /// <summary>Gets or initializes the deterministic corpus and request-selection seed.</summary>
    public int Seed { get; init; } = 1729;
    /// <summary>Gets or initializes the number of documents in the shared corpus.</summary>
    public int Documents { get; init; } = 1_000;
    /// <summary>Gets or initializes the number of measured operations in each repetition.</summary>
    public int Operations { get; init; } = 1_000;
    /// <summary>Gets or initializes the number of unmeasured warmup operations per repetition.</summary>
    public int Warmup { get; init; } = 50;
    /// <summary>Gets or initializes the number of measured repetitions.</summary>
    public int Repetitions { get; init; } = 3;
    /// <summary>Gets or initializes the maximum number of concurrent workers.</summary>
    public int Concurrency { get; init; } = 8;
    /// <summary>Gets or initializes the target serialized document payload size in bytes.</summary>
    public int PayloadBytes { get; init; } = 1_024;
    /// <summary>Gets or initializes the number of dimensions in each generated vector.</summary>
    public int Dimensions { get; init; } = 32;
    /// <summary>Gets or initializes the number of results retained by exact-neighbor workloads.</summary>
    public int TopK { get; init; } = 10;
    /// <summary>Gets or initializes the per-operation timeout budget in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;
    /// <summary>Gets or initializes the maximum number of generated graph vertices.</summary>
    public int GraphVertices { get; init; } = 256;
    /// <summary>Gets or initializes the generated outgoing edge count per graph vertex, subject to component size.</summary>
    public int GraphFanOut { get; init; } = 3;
    /// <summary>Gets or initializes the maximum graph traversal depth.</summary>
    public int GraphDepth { get; init; } = 3;

    /// <summary>Reads and validates benchmark settings from the <c>Benchmarks</c> configuration section.</summary>
    /// <param name="configuration">The configuration source containing benchmark settings.</param>
    /// <returns>The validated settings, or the default settings when the section is absent.</returns>
    public static ComparisonOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = configuration.GetSection(ConfigurationSection).Get<ComparisonOptions>() ?? new();
        options.Validate();
        return options;
    }

    /// <summary>Checks each configured option against its supported individual range.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Topology) || Documents is < 1 or > 1_000_000 || Operations is < 1 or > 1_000_000 || Warmup is < 0 or > 100_000
            || Repetitions is < 1 or > 20 || Concurrency is < 1 or > 128 || PayloadBytes is < 128 or > 65_536
            || Dimensions is < 2 or > 1_024 || TopK < 1 || TopK > Math.Min(Documents, 100)
            || TimeoutSeconds is < 1 or > 120 || GraphVertices is < 1 or > 512 || GraphFanOut is < 1 or > 8 || GraphDepth is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(ComparisonOptions), "The benchmark configuration exceeds its budgets.");
        }
    }
}

/// <summary>A generated corpus document and its exact-search vector.</summary>
/// <param name="Number">The numeric document position used to generate the document.</param>
/// <param name="Id">The deterministic document identifier.</param>
/// <param name="Json">The serialized document payload.</param>
/// <param name="Vector">The generated float32 vector associated with the document.</param>
public sealed record BenchmarkDocument(int Number, string Id, string Json, [property: JsonRequired] ImmutableArray<float> Vector);

/// <summary>A directed edge in the deterministic benchmark graph.</summary>
/// <param name="Id">The deterministic edge identifier.</param>
/// <param name="From">The source document identifier.</param>
/// <param name="To">The destination document identifier.</param>
public sealed record BenchmarkEdge(string Id, string From, string To);

/// <summary>A document returned by a comparison target.</summary>
/// <param name="Id">The returned document identifier.</param>
/// <param name="Json">The returned serialized document payload.</param>
public sealed record FoundDocument(string Id, string Json);

/// <summary>An event returned by a comparison target.</summary>
/// <param name="EventId">The identifier assigned to the event.</param>
/// <param name="Revision">The event revision reported by the target.</param>
/// <param name="Json">The returned serialized event payload.</param>
public sealed record FoundEvent(Guid EventId, ulong Revision, string Json);

/// <summary>Queue phase durations reported for one completed queue cycle.</summary>
/// <param name="EnqueueMs">Elapsed enqueue time in milliseconds.</param>
/// <param name="ReceiveMs">Elapsed receive time in milliseconds.</param>
/// <param name="AckMs">Elapsed acknowledgement time in milliseconds.</param>
public sealed record QueueTimings(double EnqueueMs, double ReceiveMs, double AckMs);

/// <summary>Holds the optional outputs produced while executing one workload operation.</summary>
/// <param name="Document">The document returned by a point-read operation, when present.</param>
/// <param name="Neighbors">The documents returned by a neighbor-search operation, when present.</param>
/// <param name="Message">The message returned by a queue operation, when present.</param>
/// <param name="Queue">The measured queue phase durations, when present.</param>
/// <param name="Vertices">The vertex identifiers returned by a traversal, when present.</param>
/// <param name="Event">The event returned by a stream operation, when present.</param>
public sealed record OperationResult(FoundDocument? Document = null, ImmutableArray<FoundDocument>? Neighbors = null,
    FoundDocument? Message = null, QueueTimings? Queue = null, ImmutableArray<string>? Vertices = null, FoundEvent? Event = null);

/// <summary>Records the observed cluster shape and state for a target profile.</summary>
/// <param name="Nodes">The number of nodes reported for the cluster.</param>
/// <param name="DataCopies">The number of data copies reported by the target.</param>
/// <param name="State">The target-reported cluster state.</param>
/// <param name="Observations">Additional target-reported cluster observations.</param>
public sealed record ClusterEvidence(int Nodes, int DataCopies, string State,
    [property: JsonRequired] ImmutableArray<string> Observations);

/// <summary>Describes a comparison target's version, topology, and operation contracts.</summary>
/// <param name="Name">The target name shown in comparison results.</param>
/// <param name="Version">The target version reported for this run.</param>
/// <param name="Topology">The target's reported topology description.</param>
/// <param name="WriteAcknowledgement">The target's write acknowledgement contract.</param>
/// <param name="ReadContract">The target's read consistency or visibility contract.</param>
/// <param name="Transport">The transport used to reach the target.</param>
/// <param name="Authorization">The authorization mode used for the target.</param>
/// <param name="Image">The container image reference, when the target uses a container.</param>
public sealed record TargetProfile(string Name, string Version, string Topology, string WriteAcknowledgement,
    string ReadContract, string Transport, string Authorization, string? Image)
{
    /// <summary>Gets or initializes evidence describing the observed target cluster, when available.</summary>
    public ClusterEvidence? Cluster { get; init; }
}

/// <summary>Defines the lifecycle and capabilities of a comparison database target.</summary>
public interface IComparisonTarget : IAsyncDisposable
{
    /// <summary>Gets the target's descriptive profile.</summary>
    TargetProfile Profile { get; }
    /// <summary>Determines whether the target supports the specified workload scenario.</summary>
    /// <param name="scenario">The workload scenario to check.</param>
    /// <returns><see langword="true"/> when the target supports the scenario; otherwise, <see langword="false"/>.</returns>
    bool Supports(Scenario scenario);
    /// <summary>Gets the reason a scenario is outside the target's shared contract.</summary>
    string UnsupportedReason => "Outside this target's shared contract.";
    /// <summary>Initializes the target with the benchmark corpus.</summary>
    /// <param name="dataset">The deterministic corpus and workload oracle.</param>
    /// <param name="cancellationToken">A token used to cancel initialization.</param>
    /// <returns>A task that completes when initialization finishes.</returns>
    Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken);
    /// <summary>Opens a session for executing operations against the target.</summary>
    /// <param name="cancellationToken">A token used to cancel session creation.</param>
    /// <returns>A task whose result is the opened comparison session.</returns>
    Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken);
}

/// <summary>Executes workload operations within one comparison target session.</summary>
public interface IComparisonSession : IAsyncDisposable
{
    /// <summary>Executes the selected workload operation for a document.</summary>
    /// <param name="scenario">The workload operation to execute.</param>
    /// <param name="document">The document used as the operation input.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task whose result contains any operation outputs.</returns>
    Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken);
    /// <summary>Reads the specified document from the target.</summary>
    /// <param name="document">The document whose identifier is read.</param>
    /// <param name="cancellationToken">A token used to cancel the read.</param>
    /// <returns>A task whose result is the found document, or <see langword="null"/> when it is absent.</returns>
    Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken);
    /// <summary>Reads the event associated with the specified document.</summary>
    /// <param name="document">The document identifying the event to read.</param>
    /// <param name="cancellationToken">A token used to cancel the read.</param>
    /// <returns>A task whose result is the found event, or <see langword="null"/> when it is absent.</returns>
    Task<FoundEvent?> ReadEventAsync(BenchmarkDocument document, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>Captures one operation's timing, outcome, payload, and queue measurements.</summary>
/// <param name="Operation">The operation index within the workload repetition.</param>
/// <param name="Worker">The worker index that executed the operation.</param>
/// <param name="StartedMs">The operation start timestamp in elapsed milliseconds.</param>
/// <param name="CompletedMs">The operation completion timestamp in elapsed milliseconds.</param>
/// <param name="Success">Whether the operation completed successfully.</param>
/// <param name="Error">The error description when the operation failed.</param>
/// <param name="PayloadBytes">The payload size associated with the operation, in bytes.</param>
/// <param name="CompletedMessageId">The identifier of a completed message, when applicable.</param>
/// <param name="Queue">The queue phase timings, when the operation measured a queue cycle.</param>
public sealed record OperationSample(int Operation, int Worker, double StartedMs, double CompletedMs,
    bool Success, string? Error, int PayloadBytes, string? CompletedMessageId, QueueTimings? Queue)
{
    /// <summary>Gets the elapsed operation latency in milliseconds.</summary>
    public double LatencyMs => CompletedMs - StartedMs;
}

/// <summary>Contains the 50th, 95th, and 99th percentile latency values.</summary>
/// <param name="P50Ms">The 50th percentile latency in milliseconds.</param>
/// <param name="P95Ms">The 95th percentile latency in milliseconds.</param>
/// <param name="P99Ms">The 99th percentile latency in milliseconds.</param>
public sealed record Latencies(double P50Ms, double P95Ms, double P99Ms);

/// <summary>Reports resource counters sampled from the load-generator process during a measurement; they do not describe server use or attribute costs to individual operations.</summary>
/// <param name="CpuSeconds">CPU time consumed by the load-generator process during collection, in seconds.</param>
/// <param name="AllocatedBytes">The number of bytes allocated by the load-generator process during collection, not an allocation total attributed to individual operations.</param>
/// <param name="PeakObservedWorkingSetBytes">The largest load-generator process working-set value observed at a sampling point, in bytes; this may be below the process's true peak.</param>
/// <param name="SamplingIntervalMs">The interval between load-generator process resource samples, in milliseconds.</param>
public sealed record ClientResources(double CpuSeconds, long AllocatedBytes, long PeakObservedWorkingSetBytes, int SamplingIntervalMs);

/// <summary>Aggregates operation counts, throughput, latency, and optional resource timings.</summary>
/// <param name="Attempts">The number of attempted operations.</param>
/// <param name="Successes">The number of successful operations.</param>
/// <param name="Failures">The number of failed operations.</param>
/// <param name="ElapsedSeconds">The measured elapsed duration, in seconds.</param>
/// <param name="UsefulOperationsPerSecond">The useful completed operation rate per second.</param>
/// <param name="Latency">The overall operation latency percentiles.</param>
/// <param name="UniqueCompletedMessages">The number of unique completed messages.</param>
/// <param name="Enqueue">The enqueue latency percentiles, when measured.</param>
/// <param name="Receive">The receive latency percentiles, when measured.</param>
/// <param name="Ack">The acknowledgement latency percentiles, when measured.</param>
/// <param name="ClientResources">The load-generator process resource counters sampled during the measurement, when collected.</param>
public sealed record Measurement(int Attempts, int Successes, int Failures, double ElapsedSeconds,
    double UsefulOperationsPerSecond, Latencies Latency, int UniqueCompletedMessages,
    Latencies? Enqueue, Latencies? Receive, Latencies? Ack, ClientResources? ClientResources = null);
/// <summary>Stores the outcome and operation samples for one target, scenario, and repetition.</summary>
/// <param name="Target">The target name.</param>
/// <param name="Scenario">The workload scenario.</param>
/// <param name="Repetition">The repetition index.</param>
/// <param name="Status">The outcome status recorded by the harness.</param>
/// <param name="Detail">Additional status detail, when present.</param>
/// <param name="Measurement">The aggregate measurements, when the scenario ran.</param>
/// <param name="Samples">The individual operation samples.</param>
public sealed record ComparisonCase(string Target, Scenario Scenario, int Repetition, string Status,
    string? Detail, Measurement? Measurement, [property: JsonRequired] ImmutableArray<OperationSample> Samples);

/// <summary>Contains the configuration, environment, targets, and cases for one comparison run.</summary>
/// <param name="SchemaVersion">The serialized report schema version.</param>
/// <param name="RunId">The unique identifier for the comparison run.</param>
/// <param name="StartedAt">The run start time.</param>
/// <param name="Options">The validated workload configuration.</param>
/// <param name="DatasetSha256">The SHA-256 hash of the generated dataset.</param>
/// <param name="LoadModel">The harness load model description.</param>
/// <param name="HostOs">The operating system running the harness.</param>
/// <param name="Architecture">The processor architecture running the harness.</param>
/// <param name="LogicalProcessors">The number of logical processors reported by the host.</param>
/// <param name="Runtime">The runtime description reported for the run.</param>
/// <param name="Storage">The storage description reported for the run.</param>
/// <param name="SourceRevision">The source revision associated with the run, when known.</param>
/// <param name="Targets">The target profiles included in the run.</param>
/// <param name="Cases">The target/scenario repetition results.</param>
public sealed record ComparisonReport(int SchemaVersion, Guid RunId, DateTimeOffset StartedAt,
    ComparisonOptions Options, string DatasetSha256, string LoadModel, string HostOs, string Architecture,
    int LogicalProcessors, string Runtime, string Storage, string? SourceRevision,
    [property: JsonRequired] ImmutableArray<TargetProfile> Targets,
    [property: JsonRequired] ImmutableArray<ComparisonCase> Cases)
{
    /// <summary>Gets or initializes GitHub Actions provenance for the comparison run.</summary>
    public GitHubProvenance? Provenance { get; init; }
    /// <summary>Gets or initializes the load-generator container image reference, when used.</summary>
    public string? LoadGeneratorImage { get; init; }
}

/// <summary>Identifies the GitHub Actions workflow run and selected comparison profile.</summary>
/// <param name="RunId">The GitHub Actions run identifier.</param>
/// <param name="Attempt">The run attempt number.</param>
/// <param name="Repository">The repository that owns the workflow run.</param>
/// <param name="Ref">The Git reference used by the run.</param>
/// <param name="Workflow">The workflow name.</param>
/// <param name="Profile">The selected comparison profile.</param>
public sealed record GitHubProvenance(long RunId, int Attempt, string Repository, string Ref, string Workflow, string Profile);
