using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionState(BenchmarkSessionOptions options)
{
    internal bool Loaded
    {
        get; set;
    }
    internal bool Mixed
    {
        get; set;
    }
    internal bool Poisoned
    {
        get; private set;
    }
    internal string? UnsupportedDocumentContract
    {
        get; set;
    }
    internal bool Ready => Loaded && !Poisoned;
    internal BenchmarkSessionCaseReport Unavailable(string id, string detail) => Report(id, BenchmarkSessionProtocol.Unavailable, detail, BenchmarkSessionLimits.NoObservedItems, [], BenchmarkSessionLimits.NoObservedItems);
    internal BenchmarkSessionCaseReport Prerequisite(string id)
        => UnsupportedDocumentContract is { } reason ? Unavailable(id, reason)
            : Report(id, BenchmarkSessionProtocol.Failed, BenchmarkSessionProtocol.LiveSessionPrerequisiteFailedOrNotExecuted, BenchmarkSessionLimits.NoObservedItems, [], BenchmarkSessionLimits.NoObservedItems);
    internal BenchmarkSessionCaseReport Failed(string id, string detail, BenchmarkSessionTimer timer, ImmutableArray<BenchmarkSessionLaneReport> lanes)
    {
        Poisoned = true;
        return Report(id, BenchmarkSessionProtocol.Failed, detail, BenchmarkSessionLimits.NoObservedItems, lanes, timer.Elapsed.TotalSeconds);
    }
    internal BenchmarkSessionCaseReport Report(string id, string status, string? detail, int verified,
        ImmutableArray<BenchmarkSessionLaneReport> lanes, double seconds)
        => new(id, status, detail, id == BenchmarkSessionProtocol.LoadMillion ? BenchmarkSessionLimits.NoObservedItems : options.Records, options.Records, verified, lanes, seconds);
}
