using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>One immutable native test outcome from a shared live database, without publication authority.</summary>
public sealed record BenchmarkSessionCaseReport(string CaseId, string Status, string? Detail,
    int InitialRecords, int ExpectedRecords, int VerifiedRecords,
    ImmutableArray<BenchmarkSessionLaneReport> Lanes, double ElapsedSeconds);

/// <summary>A bounded native operation stream; timestamps share the enclosing case's monotonic origin.</summary>
public sealed record BenchmarkSessionLaneReport(string Id, Measurement Measurement,
    double FirstStartedMs, double LastCompletedMs, ImmutableArray<BenchmarkSessionOperationSample> Samples);

/// <summary>Original bounded operation timing from the shared case origin, retained for independent aggregation.</summary>
public readonly record struct BenchmarkSessionOperationSample(int Operation, int Worker, double StartedMs,
    double CompletedMs, bool Success, string? Error);
