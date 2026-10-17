namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

internal sealed record BenchmarkSessionRuntime(string OperatingSystem, string Architecture, int LogicalProcessors,
    string Framework, string Storage);
