using System.Globalization;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal enum NativeLogSubscriptionPhase { Running, Completed, Canceled, Faulted }

internal sealed class NativeLogSubscriptionEvidence(string nodeName)
{
    private readonly Lock gate = new();
    private NativeLogSubscriptionPhase state = NativeLogSubscriptionPhase.Running;
    internal DateTimeOffset StartedAt { get; } = TimeProvider.System.GetUtcNow();
    private DateTimeOffset? first;
    private DateTimeOffset? last;
    internal string NodeName => nodeName;

    internal void Received()
    {
        lock (gate)
        {
            var observedAt = TimeProvider.System.GetUtcNow();
            first ??= observedAt;
            last = observedAt;
        }
    }

    internal void Terminal(NativeLogSubscriptionPhase terminal)
    {
        lock (gate)
        {
            if (state == NativeLogSubscriptionPhase.Running)
            { state = terminal; }
        }
    }

    internal string Snapshot()
    {
        lock (gate)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{nodeName}: native log subscription {state}, startedObserved={StartedAt:O}, firstObserved={first:O}, lastObserved={last:O}");
        }
    }
}
