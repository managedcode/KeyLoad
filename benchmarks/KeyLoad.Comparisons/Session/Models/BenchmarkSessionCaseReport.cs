using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>One immutable native test outcome from a shared live database, without publication authority.</summary>
public sealed record BenchmarkSessionCaseReport(string CaseId, string Status, string? Detail,
    int InitialRecords, int ExpectedRecords, int VerifiedRecords,
    ImmutableArray<BenchmarkSessionLaneReport> Lanes, double ElapsedSeconds);
