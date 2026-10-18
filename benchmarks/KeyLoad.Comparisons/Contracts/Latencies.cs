namespace KeyLoad.Comparisons;

/// <summary>Contains the 50th, 95th, and 99th percentile latency values.</summary>
/// <param name="P50Ms">The 50th percentile latency in milliseconds.</param>
/// <param name="P95Ms">The 95th percentile latency in milliseconds.</param>
/// <param name="P99Ms">The 99th percentile latency in milliseconds.</param>
public sealed record Latencies(double P50Ms, double P95Ms, double P99Ms);
