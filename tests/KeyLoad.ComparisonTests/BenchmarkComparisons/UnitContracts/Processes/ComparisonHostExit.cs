namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Captures a bounded real comparison-host startup result.</summary>
/// <param name="ExitCode">Child-process exit code.</param>
/// <param name="Stdout">Bounded drained standard output.</param>
/// <param name="Stderr">Bounded drained standard error.</param>
internal sealed record ComparisonHostExit(int ExitCode, string Stdout, string Stderr);
