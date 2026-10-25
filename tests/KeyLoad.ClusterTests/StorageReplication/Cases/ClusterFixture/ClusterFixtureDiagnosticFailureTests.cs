using System.Collections.Concurrent;
using System.Text;

namespace KeyLoad.ClusterTests.StorageReplication;

/// <summary>Retains the real closed execution rejection through shutdown noise and final fixture projection.</summary>
internal sealed class ClusterFixtureDiagnosticFailureTests
{
    private const string Node = "node1";
    private const string Rejection = "Orleans request rejected: 00000000-0000-0000-0000-000000000001 at CommandDispatch (Unexpected) with UnknownWriteOutcome; movement guard None; parent operands None. Closed cause State.";
    private const string Shutdown = "at Orleans.LifecycleSubject.OnStop(CancellationToken cancellationToken)";
    private const string Rendering = "Unhandled exception rendering component: Cannot access a disposed object.";
    private const int MaximumLines = 80;
    private const int MaximumBytes = 8_192;
    private const int PriorityLines = 3;
    private const int Overflow = 1;

    [Test]
    public async Task OriginalRejectionSurvivesTailEvictionAndFinalOwnedProjection()
    {
        var tail = new ConcurrentQueue<string>();
        var failures = new ConcurrentQueue<string>();
        BoundedDiagnosticLog.CaptureLogLine(tail, failures, Rejection);
        BoundedDiagnosticLog.CaptureLogLine(tail, failures, Rendering);
        for (var index = 0; index < ClusterFixtureProtocol.CapturedLogLinesPerNode + Overflow; index++)
        {
            BoundedDiagnosticLog.CaptureLogLine(tail, failures, Shutdown);
        }

        await Assert.That(tail.ToArray().Contains(Rejection, StringComparer.Ordinal)).IsFalse();
        await Assert.That(failures.ToArray().SequenceEqual(new[] { Rejection, Rendering })).IsTrue();
        var output = BoundedDiagnosticLog.FormatOwnedNodeLogs([Node], [],
            _ => failures.ToArray(), _ => tail.ToArray());
        await Assert.That(output).Contains(line => line == Node + ": " + Rejection);
        await Assert.That(output).Contains(line => line == Node + ": " + Rendering);
        await Assert.That(output).Contains(line => line == Node + ": " + Shutdown);
        await Assert.That(tail.Count).IsEqualTo(ClusterFixtureProtocol.CapturedLogLinesPerNode);
        await Assert.That(failures.Count).IsLessThanOrEqualTo(PriorityLines);
        await Assert.That(output.Length).IsLessThanOrEqualTo(MaximumLines);
        await Assert.That(Encoding.UTF8.GetByteCount(string.Join(Environment.NewLine, output) + Environment.NewLine))
            .IsLessThanOrEqualTo(MaximumBytes);
    }
}
