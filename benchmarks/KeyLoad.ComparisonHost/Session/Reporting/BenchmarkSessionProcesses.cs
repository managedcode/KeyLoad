using System.Collections.Immutable;
using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Actual client and server processes observed independently of container resource qualification.</summary>
internal sealed record BenchmarkSessionProcesses(NativeProcessEvidence Client, ImmutableArray<NativeProcessEvidence> Servers);
