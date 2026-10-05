using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

internal sealed class OpenLoopExecutionContext(ScaledComparisonProfile profile, int rate,
    IComparisonTarget target, IsolatedComparisonWorker worker, string storage,
    OpenLoopExecutionPolicy executionPolicy)
{
    internal ScaledComparisonProfile Profile { get; } = profile;
    internal int Rate { get; } = rate;
    internal IComparisonTarget Target { get; } = target;
    internal IsolatedComparisonWorker Worker { get; } = worker;
    internal string Storage { get; } = storage;
    internal OpenLoopExecutionPolicy ExecutionPolicy { get; } = executionPolicy;
    internal Scenario Scenario { get; } = worker.Scenario;
    internal ScaledComparisonCorpus Corpus { get; } = new(profile);
    internal List<IComparisonSession> Sessions { get; } = new(executionPolicy.ConcurrentSessions);
    internal DateTimeOffset StartedAt { get; set; } = DateTimeOffset.MinValue;
    internal long MeasurementFinishedTimestamp { get; set; }
    internal OpenLoopTimeline Timeline { get; set; }
    internal OpenLoopRunState? State { get; set; }
    internal ClientResources? Resources { get; set; }
    internal bool CallerCancelled { get; set; }
    internal bool DrainExpired { get; set; }
    internal bool Readback { get; set; }
    internal bool SessionsClosed { get; set; }
    internal Exception? Primary { get; set; }
    internal ImmutableArray<Exception> CleanupFailures { get; set; } = ImmutableArray<Exception>.Empty;
}
