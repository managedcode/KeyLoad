using System.Text;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains bounded original commit responses without extra requests or changes to retry behavior.</summary>
internal sealed class RetainedReplicaSnapshotRetryEvidence
{
    private const int MaximumDetailBytes = 192;
    private const int MaximumIdentityBytes = 48;
    private const int MaximumLeaderBytes = 96;
    private const int MaximumRecordBytes = 8_192;
    private long started;
    private long completed;
    private long refused;
    private Failure? first;
    private Failure? last;

    internal void BeginAttempt() => started++;

    internal void Complete(bool success, string? code, string? detail)
    {
        completed++;
        if (success)
        {
            return;
        }
        refused++;
        last = new(started, TimeProvider.System.GetUtcNow(), Clip(code, MaximumIdentityBytes),
            Clip(detail, MaximumDetailBytes));
        first ??= last;
    }

    internal void Write(Guid commandId, int ordinal, int survivor, int stopped, NodeStatus[] initial)
    {
        var json = JsonSerializer.Serialize(new
        {
            phase = nameof(RetainedReplicaSnapshotProduction.ReplicateWhileStoppedAsync),
            commandId,
            commandOrdinal = ordinal,
            survivorNode = ClusterReplicationTestSupport.NodeName(survivor + 1),
            stoppedNode = ClusterReplicationTestSupport.NodeName(stopped + 1),
            callsStarted = started,
            responsesCompleted = completed,
            refusedResponses = refused,
            first,
            last,
            initialStatuses = initial.Select((status, index) => new
            {
                node = ClusterReplicationTestSupport.NodeName(index + 1),
                nodeId = Clip(status.NodeId, MaximumIdentityBytes),
                status.Incarnation,
                status.Applied,
                leader = Clip(status.Leader, MaximumLeaderBytes),
                status.Voters,
                status.RoutingReady,
                status.ConsensusTerm,
                status.ReadGeneration
            })
        });
        if (Encoding.UTF8.GetByteCount(json) > MaximumRecordBytes)
        {
            throw new InvalidOperationException("The original snapshot retry evidence exceeds its bounded record.");
        }
        Console.Error.WriteLine(json);
    }

    private static string? Clip(string? value, int maximumBytes)
        => value is null ? null : BoundedDiagnosticLog.ClipUtf8(value, maximumBytes);

    private sealed record Failure(long Attempt, DateTimeOffset ObservedAt, string? ErrorCode, string? SafeDetail);
}
