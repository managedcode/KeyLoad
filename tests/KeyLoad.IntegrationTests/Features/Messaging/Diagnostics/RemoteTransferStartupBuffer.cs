using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferStartupBuffer(string name)
{
    private const int TailRecords = 12;
    private const int FirstFailureRecords = 6;
    private const int MaximumLineBytes = 1_024;
    private const string FailurePrefix = "fail:";
    private const string CriticalPrefix = "crit:";
    private const string UnhandledPrefix = "Unhandled exception";
    private const string PrerequisitePrefix = "NativeReplicaPrerequisite failed";
    private const string Unobserved = "Unobserved";
    private readonly Lock gate = new();
    private readonly Queue<(long Ordinal, string Text)> tail = new();
    private readonly List<(long Ordinal, string Text)> first = new();
    private long observed;
    private bool startedFailure;
    private string state = Unobserved;
    private int? exit;
    private string health = Unobserved;
    private string terminal = Unobserved;

    internal void State(string? value, int? code, string? status)
    {
        lock (gate)
        { state = value ?? Unobserved; exit = code; health = status ?? Unobserved; }
    }

    internal void Record(string original)
    {
        var bounded = BoundedDiagnosticLog.ClipUtf8(original, MaximumLineBytes);
        lock (gate)
        {
            observed++;
            startedFailure |= original.Contains(FailurePrefix, StringComparison.Ordinal)
                || original.Contains(CriticalPrefix, StringComparison.Ordinal)
                || original.Contains(UnhandledPrefix, StringComparison.Ordinal)
                || original.Contains(PrerequisitePrefix, StringComparison.Ordinal);
            if (startedFailure && first.Count < FirstFailureRecords)
            { first.Add((observed, bounded)); }
            tail.Enqueue((observed, bounded));
            while (tail.Count > TailRecords)
            { tail.Dequeue(); }
        }
    }

    internal void Terminal(string value) { lock (gate) { terminal = value; } }

    internal string[] Snapshot(string boundary, string primary, ErrorCode? code, bool cancelled)
    {
        lock (gate)
        {
            return BoundedDiagnosticLog.Bound(new[]
            {
                $"Resource={name}; Boundary={boundary}; PrimaryCategory={primary}; PrimaryCode={code}; CallerCancelled={cancelled}",
                $"State={state}; Exit={exit}; Health={health}; StreamTerminal={terminal}; ObservedLines={observed}; TailRetained={tail.Count}; FirstFailureRetained={first.Count}; DroppedLines={observed - first.Concat(tail).Select(value => value.Ordinal).Distinct().LongCount()}",
                "First original failure context follows; absence remains unobserved."
            }.Concat(first.Select(value => value.Text)).Append("Original bounded terminal tail follows.").Concat(tail.Select(value => value.Text)));
        }
    }
}
