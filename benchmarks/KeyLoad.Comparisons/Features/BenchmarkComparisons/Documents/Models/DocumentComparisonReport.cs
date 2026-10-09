using System.Collections.Immutable;
namespace KeyLoad.Comparisons;

/// <summary>Original complete document-v1 repetitions; this report alone is not authenticated publication evidence.</summary>
public sealed record DocumentComparisonReport(int SchemaVersion, string Family, DocumentComparisonSelection Selection,
    string Status, bool Qualified, ImmutableArray<DocumentComparisonRepetition> Repetitions);
/// <summary>One fresh namespace, native client admission, complete operation accounting and independently verified final state.</summary>
public sealed record DocumentComparisonRepetition(int Repetition, TargetProfile? Target, string Status, bool Qualified,
    ImmutableArray<string> Errors, int InitialRecords, int AddedRecords, int DeletedRecords, int ExpectedFinalRecords, int ActualFinalRecords,
    string? ExpectedSha256, string? ActualSha256, int RequestedClients, int OpenedClients, int PeakInFlight,
    long Planned, long Attempts, long Acknowledged, long Failed, long Canceled, long Unfinished,
    bool SingleOperationTiming, DocumentComparisonPhases Phases, DocumentLatencyHistogram Histogram,
    ClientResources? ClientResources);
/// <summary>Independently recorded native execution phases in seconds.</summary>
public sealed record DocumentComparisonPhases(double SetupSeconds, double WarmupSeconds, double MeasuredSeconds,
    double VerificationSeconds, double CleanupSeconds, double? LoadSeconds, double? IndexBuildSeconds,
    bool IndexBuildApplicable, bool PhaseInstrumentationComplete);
/// <summary>Sparse nonzero histogram buckets retain every attempted operation; percentiles are bucket upper bounds.</summary>
public sealed record DocumentLatencyHistogram(int ResolutionMicroseconds, int MaximumMilliseconds,
    long Count, long OverflowCount, ImmutableArray<int> BucketIndices, ImmutableArray<long> BucketCounts, double P50Milliseconds,
    double P95Milliseconds, double P99Milliseconds, double MaximumObservedMilliseconds);
