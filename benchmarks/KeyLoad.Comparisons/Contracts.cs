using Microsoft.Extensions.Configuration;

namespace KeyLoad.Comparisons;

public enum Scenario { PointRead, DocumentWrite, VectorExact, QueueCycle }

public sealed record ComparisonOptions
{
    public int Seed { get; init; } = 1729;
    public int Documents { get; init; } = 1_000;
    public int Operations { get; init; } = 1_000;
    public int Warmup { get; init; } = 50;
    public int Repetitions { get; init; } = 3;
    public int Concurrency { get; init; } = 8;
    public int PayloadBytes { get; init; } = 1_024;
    public int Dimensions { get; init; } = 32;
    public int TopK { get; init; } = 10;
    public int TimeoutSeconds { get; init; } = 30;

    public static ComparisonOptions Read(IConfiguration configuration)
    {
        var options = configuration.GetSection("Benchmarks").Get<ComparisonOptions>() ?? new();
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (Documents is < 1 or > 1_000_000 || Operations is < 1 or > 1_000_000 || Warmup is < 0 or > 100_000
            || Repetitions is < 1 or > 20 || Concurrency is < 1 or > 128 || PayloadBytes is < 128 or > 65_536
            || Dimensions is < 2 or > 1_024 || TopK < 1 || TopK > Math.Min(Documents, 100)
            || TimeoutSeconds is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(ComparisonOptions), "The benchmark configuration exceeds its budgets.");
    }
}

public sealed record BenchmarkDocument(int Number, string Id, string Json, float[] Vector);
public sealed record FoundDocument(string Id, string Json);
public sealed record QueueTimings(double EnqueueMs, double ReceiveMs, double AckMs);
public sealed record OperationResult(FoundDocument? Document = null, FoundDocument[]? Neighbors = null,
    FoundDocument? Message = null, QueueTimings? Queue = null);
public sealed record TargetProfile(string Name, string Version, string Topology, string WriteAcknowledgement,
    string ReadContract, string Transport, string Authorization, string? Image);

public interface IComparisonTarget : IAsyncDisposable
{
    TargetProfile Profile { get; }
    bool Supports(Scenario scenario);
    Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken);
    Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken);
}

public interface IComparisonSession : IAsyncDisposable
{
    Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken);
    Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken);
}

public sealed record OperationSample(int Operation, int Worker, double StartedMs, double CompletedMs,
    bool Success, string? Error, int PayloadBytes, string? CompletedMessageId, QueueTimings? Queue)
{
    public double LatencyMs => CompletedMs - StartedMs;
}

public sealed record Latencies(double P50Ms, double P95Ms, double P99Ms);
public sealed record Measurement(int Attempts, int Successes, int Failures, double ElapsedSeconds,
    double UsefulOperationsPerSecond, Latencies Latency, int UniqueCompletedMessages,
    Latencies? Enqueue, Latencies? Receive, Latencies? Ack);
public sealed record ComparisonCase(string Target, Scenario Scenario, int Repetition, string Status,
    string? Detail, Measurement? Measurement, OperationSample[] Samples);
public sealed record ComparisonReport(int SchemaVersion, Guid RunId, DateTimeOffset StartedAt,
    ComparisonOptions Options, string DatasetSha256, string LoadModel, string HostOs, string Architecture,
    int LogicalProcessors, string Runtime, string Storage, string? SourceRevision, TargetProfile[] Targets, ComparisonCase[] Cases);

public sealed class ComparisonFailure(string code) : Exception(code);
