namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>Detached identity of one observed native process; it establishes no resource equivalence or readiness.</summary>
/// <param name="Name">The actual owning resource or workload-client name.</param>
/// <param name="ProcessId">The observed operating-system process identifier.</param>
/// <param name="StartedAtUtc">The operating-system process startup timestamp.</param>
/// <param name="AspireStartedAtUtc">The Aspire startup timestamp, when Aspire owns this process.</param>
/// <param name="Binary">The observed executable, managed assembly, symbols and compilation source identity.</param>
public sealed record NativeProcessEvidence(string Name, int ProcessId, DateTime StartedAtUtc,
    DateTime? AspireStartedAtUtc, NativeBinaryEvidence Binary);
