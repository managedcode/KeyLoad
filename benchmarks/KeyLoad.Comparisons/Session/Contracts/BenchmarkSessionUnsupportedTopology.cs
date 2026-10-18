using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>One explicitly unsupported native topology and its owning capability reason.</summary>
public sealed record BenchmarkSessionUnsupportedTopology(string Target, ImmutableArray<int> NodeCounts, string Reason);
