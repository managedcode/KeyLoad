namespace KeyLoad.Comparisons;

/// <summary>A directed edge in the deterministic benchmark graph.</summary>
/// <param name="Id">The deterministic edge identifier.</param>
/// <param name="From">The source document identifier.</param>
/// <param name="To">The destination document identifier.</param>
public sealed record BenchmarkEdge(string Id, string From, string To);
