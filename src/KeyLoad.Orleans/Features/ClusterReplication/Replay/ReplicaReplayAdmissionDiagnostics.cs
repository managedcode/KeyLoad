using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed partial class ReplicaReplayAdmissionDiagnostics(ILogger logger, int voterCount,
    IOptions<ReplicaTransportOptions> transportOptions)
{
    private const int RatesStartEmptyCount = 0;

    private const int CapacityEvent = 2101;
    private const int ConfigurationEvent = 2102;
    private readonly long intervalMilliseconds = checked((long)transportOptions.Value.ReplayDiagnosticInterval.TotalMilliseconds);
    private const string CapacityMessage = "ReplicaReplayCapacity senderIndex={SenderIndex} pool={Pool} method={Method} "
        + "critical={CriticalCount} forward={ForwardCount} read={ReadCount} data={DataCount} capacity={Capacity} "
        + "voterMaximum={VoterMaximum} nodeMaximum={NodeMaximum} observedUnixMs={Observed} oldestExpiryUnixMs={OldestExpiry} suppressed={Suppressed}";
    private const string ConfigurationMessage = "ReplicaReplayConfigured voters={Voters} critical={Critical} forward={Forward} "
        + "read={Read} data={Data} nodeMaximum={NodeMaximum}";
    private static readonly int PoolCount = Enum.GetValues<ReplicaReplayPool>().Length;
    private readonly Lock gate = new();
    private readonly Rate[] rates = Enumerable.Range(RatesStartEmptyCount, checked(voterCount * PoolCount)).Select(_ => new Rate()).ToArray();

    internal void Configured(ReplicaReplayLimits limits)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            var nodeMaximum = limits.MaximumRetainedNonces(voterCount);
            Configuration(logger, voterCount, limits.CriticalPerVoter, limits.ForwardPerVoter,
                limits.ReadBarrierPerVoter, limits.DataAppendPerVoter, nodeMaximum);
        }
    }

    internal void Report(ReplicaReplayAdmissionFailure failure)
    {
        const int SuppressedEmptyCount = 0;

        long suppressed;
        lock (gate)
        {
            var rate = rates[checked(failure.SenderIndex * PoolCount + (int)failure.Pool)];
            if (rate.Emitted && failure.ObservedUnixMilliseconds - rate.Last < intervalMilliseconds)
            {
                if (rate.Suppressed < long.MaxValue)
                { rate.Suppressed++; }
                return;
            }
            suppressed = rate.Suppressed;
            rate.Emitted = true;
            rate.Last = failure.ObservedUnixMilliseconds;
            rate.Suppressed = SuppressedEmptyCount;
        }
        Emit(failure, suppressed);
    }

    private void Emit(ReplicaReplayAdmissionFailure failure, long suppressed)
    {
        try
        {
            Capacity(logger, failure.SenderIndex, (int)failure.Pool, (int)failure.Method, failure.CriticalCount,
                failure.ForwardCount, failure.ReadCount, failure.DataCount, failure.Capacity, failure.VoterMaximum,
                failure.NodeMaximum, failure.ObservedUnixMilliseconds, failure.OldestExpiryUnixMilliseconds, suppressed);
        }
        catch (AggregateException)
        {
            // LoggerFactory aggregates provider failures; diagnostics cannot replace the authoritative quota denial.
        }
    }

    [LoggerMessage(EventId = CapacityEvent, Level = LogLevel.Warning, Message = CapacityMessage)]
    private static partial void Capacity(ILogger logger, int senderIndex, int pool, int method, int criticalCount,
        int forwardCount, int readCount, int dataCount, int capacity, int voterMaximum, int nodeMaximum,
        long observed, long oldestExpiry, long suppressed);

    [LoggerMessage(EventId = ConfigurationEvent, Level = LogLevel.Information, Message = ConfigurationMessage)]
    private static partial void Configuration(ILogger logger, int voters, int critical, int forward, int read, int data, int nodeMaximum);

    private sealed class Rate
    {
        internal bool Emitted { get; set; }
        internal long Last { get; set; }
        internal long Suppressed { get; set; }
    }
}
