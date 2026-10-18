namespace KeyLoad.Comparisons;

/// <summary>Original bounded operation timing from the shared case origin, retained for independent aggregation.</summary>
public readonly record struct BenchmarkSessionOperationSample(int Operation, int Worker, double StartedMs,
    double CompletedMs, bool Success, string? Error);
