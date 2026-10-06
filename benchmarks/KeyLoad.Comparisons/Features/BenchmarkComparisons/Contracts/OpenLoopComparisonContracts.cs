using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>Describes the terminal disposition of one fixed-rate scheduled position.</summary>
public enum OpenLoopOutcome
{
    /// <summary>The position was never offered to the bounded queue.</summary>
    NotOffered,
    /// <summary>The bounded queue rejected the immediate offer.</summary>
    HarnessRejected,
    /// <summary>The accepted position expired before a native call started.</summary>
    TimedOutBeforeStart,
    /// <summary>The native operation and its result check succeeded before its arrival deadline.</summary>
    Succeeded,
    /// <summary>The native operation or its result check failed.</summary>
    Failed,
    /// <summary>The native KeyLoad adapter returned its exact typed resource-exhausted response.</summary>
    TargetRejected,
    /// <summary>The started native operation reached its scheduled-arrival deadline.</summary>
    TimedOutAfterStart,
    /// <summary>The bounded drain froze a queued position before it started.</summary>
    UnfinishedQueued,
    /// <summary>The bounded drain froze a started position before it settled.</summary>
    UnfinishedStarted
}

/// <summary>Reports the exact disjoint denominator and terminal counts for one open-loop cell.</summary>
public sealed record OpenLoopOperationAccounting(int Planned, int NotOffered, int HarnessRejected,
    int TimedOutBeforeStart, int Succeeded, int Failed, int TargetRejected, int TimedOutAfterStart,
    int UnfinishedQueued, int UnfinishedStarted, int Started, int Completed);

/// <summary>One bounded deterministic sample of a scheduled operation position.</summary>
public sealed record OpenLoopLatencySample(int Index, int? Session, long DueOffsetNanoseconds,
    double? DecisionOffsetMilliseconds, double? OfferedOffsetMilliseconds, double? StartedOffsetMilliseconds,
    double? TerminalOffsetMilliseconds, int PayloadBytes, OpenLoopOutcome Outcome);

/// <summary>Sampled latency quantiles for one timing dimension, in milliseconds.</summary>
public sealed record OpenLoopLatencyQuantiles(double? P50Milliseconds, double? P95Milliseconds,
    double? P99Milliseconds, int SampleCount, int MissingSamples, int Denominator);

/// <summary>Separate bounded sampled timing dimensions for one fixed-rate measurement.</summary>
public sealed record OpenLoopTimingSummary(OpenLoopLatencyQuantiles ScheduledToTerminal,
    OpenLoopLatencyQuantiles SchedulerLag, OpenLoopLatencyQuantiles QueueDelay,
    OpenLoopLatencyQuantiles ServiceTime, double SuccessfulOperationsPerSecond,
    int SampleCapacity, int CollectedSamples, int MissingSamples);

/// <summary>Reports bounded observed progress from completed native calls in one open-loop cell.</summary>
public sealed record OpenLoopProgressV1
{
    /// <summary>Creates one bounded actual native-call completion milestone.</summary>
    public OpenLoopProgressV1(long completed, long planned, long started, int offeredRatePerSecond,
        Scenario scenario)
    {
        const int NoMeasuredRate = 0;

        if (completed is <= NoMeasuredRate or > OpenLoopRateContract.PlannedOperations
            || completed % OpenLoopRateContract.ProgressInterval != NoMeasuredRate
            || planned != OpenLoopRateContract.PlannedOperations || started < completed || started > planned
            || !OpenLoopRateContract.AcceptedRates.Contains(offeredRatePerSecond)
            || scenario is not (Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate
                or Scenario.DocumentDelete))
        {
            throw new ArgumentOutOfRangeException(nameof(completed));
        }
        Completed = completed;
        Planned = planned;
        Started = started;
        OfferedRatePerSecond = offeredRatePerSecond;
        Scenario = scenario;
    }

    /// <summary>Gets observed terminal native calls.</summary>
    public long Completed { get; }
    /// <summary>Gets the fixed scheduled position count.</summary>
    public long Planned { get; }
    /// <summary>Gets native calls admitted to sessions.</summary>
    public long Started { get; }
    /// <summary>Gets the cell's offered arrival rate.</summary>
    public int OfferedRatePerSecond { get; }
    /// <summary>Gets the existing workload scenario.</summary>
    public Scenario Scenario { get; }
}

/// <summary>One internal fixed-rate native comparison measurement; it does not change report schema 3.</summary>
public sealed record OpenLoopComparisonReport(int Version, long RunId, int Attempt, long JobId, DateTimeOffset StartedAt,
    string ProfileId, int DatasetRecords, int OfferedRatePerSecond, Scenario Scenario,
    string DatasetSha256, TargetProfile Target, string? SourceRevision, string Storage,
    double ElapsedSeconds, bool CallerCancelled, bool DrainExpired, bool ScheduleComplete, bool MutationReadbackVerified,
    bool SessionsClosed, OpenLoopOperationAccounting Accounting, OpenLoopTimingSummary Timing,
    ImmutableArray<OpenLoopLatencySample> Samples, OpenLoopExecutionPolicy ExecutionPolicy)
{
    /// <summary>Gets the exact native worker identity that binds this separate measurement artifact.</summary>
    public IsolatedComparisonWorker? Worker { get; init; }
    /// <summary>Gets load-generator resource observations, which are not target-server resources.</summary>
    public ClientResources? ClientResources { get; init; }
    /// <summary>Gets the runtime that executed this comparison worker.</summary>
    public string? Runtime { get; init; }
}

internal enum OpenLoopSessionDisposition
{
    Succeeded,
    TargetRejected
}

internal sealed record OpenLoopSessionResult(OpenLoopSessionDisposition Disposition,
    OperationResult? Result = null);

internal interface IOpenLoopComparisonSession
{
    Task<OpenLoopSessionResult> ExecuteOpenLoopAsync(Scenario scenario, BenchmarkDocument document,
        CancellationToken cancellationToken);
}
