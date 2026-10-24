using System.Collections.Immutable;
using KeyLoad.Diagnostics.ResourceExecution;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Actual client and server processes observed independently of container resource qualification.</summary>
internal sealed record BenchmarkSessionProcesses(ProcessEvidence Client, ImmutableArray<ProcessEvidence> Servers);
