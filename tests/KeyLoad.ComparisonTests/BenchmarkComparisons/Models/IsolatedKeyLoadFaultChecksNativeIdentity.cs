using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Retains actual Aspire state and live process evidence; a closed process has no newly captured OS evidence.</summary>
internal sealed record IsolatedKeyLoadFaultChecksNativeIdentity(string Resource, int? ProcessId,
    DateTime? AspireStartedAtUtc, string State, int? ExitCode, string DataDirectorySha256,
    NativeProcessEvidence? Process);
