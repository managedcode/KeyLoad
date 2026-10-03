using System.Text;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-006: actual stored endpoints reject invalid read messages before protocol mutation.</summary>
internal sealed class ReadRoundGuardTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Invalid probes fail while the real apply/protocol gate is held, leaving durable term/log/cut unchanged.</summary>
    /// <param name="payload">A concrete invalid existing append JSON shape.</param>
    [Test]
    [Arguments(ReadRoundGuardPayloads.Nonempty)]
    [Arguments(ReadRoundGuardPayloads.NullEntries)]
    [Arguments(ReadRoundGuardPayloads.MissingEntries)]
    [Arguments(ReadRoundGuardPayloads.MissingLeader)]
    [Arguments(ReadRoundGuardPayloads.MissingTerm)]
    [Arguments(ReadRoundGuardPayloads.MissingPreviousIndex)]
    [Arguments(ReadRoundGuardPayloads.MissingPreviousTerm)]
    [Arguments(ReadRoundGuardPayloads.MissingCommittedIndex)]
    [Arguments(ReadRoundGuardPayloads.Duplicate)]
    [Arguments(ReadRoundGuardPayloads.Unknown)]
    [Arguments(ReadRoundGuardPayloads.CaseMutated)]
    [Arguments(ReadRoundGuardPayloads.InvalidTerm)]
    [Arguments(ReadRoundGuardPayloads.InvalidPrevious)]
    [Arguments(ReadRoundGuardPayloads.Trailing)]
    public async Task InvalidProbeCannotReachReceiverOrMutateStoredAuthority(string payload)
    {
        await using var cluster = new ReadRoundStoredCluster(1);
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        cluster.Attach();
        var node = cluster.Nodes[0];
        await node.Materializer.ProtocolGate.WaitAsync(linked.Token);
        try
        {
            var before = node.Log.State;
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => node.Consensus.HandleAsync(
                ReplicaRpc.ReadProbe, payload, linked.Token));
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(node.Log.State).IsEqualTo(before);
            await Assert.That(node.Database.LastApplied).IsEqualTo(0);
        }
        finally { node.Materializer.ProtocolGate.Release(); }
    }

    /// <summary>Control payloads must be the serialized empty string before the leader gate can run.</summary>
    /// <param name="payload">A concrete nonempty, malformed or wrong-type JSON value.</param>
    [Test]
    [Arguments(ReadRoundGuardPayloads.NonemptyString)]
    [Arguments(ReadRoundGuardPayloads.Null)]
    [Arguments(ReadRoundGuardPayloads.Object)]
    [Arguments(ReadRoundGuardPayloads.TrailingString)]
    public async Task InvalidControlReadCannotReachLeader(string payload)
    {
        await using var cluster = new ReadRoundStoredCluster(1);
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        cluster.Attach();
        var node = cluster.Nodes[0];
        await node.Materializer.ProtocolGate.WaitAsync(linked.Token);
        try
        {
            var before = node.Log.State;
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => node.Consensus.HandleAsync(
                ReplicaRpc.ControlReadBarrier, payload, linked.Token));
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(node.Log.State).IsEqualTo(before);
        }
        finally { node.Materializer.ProtocolGate.Release(); }
    }

    /// <summary>Valid typed data and control entries cannot be disguised as a probe even when their operation bytes decode.</summary>
    /// <param name="index">The existing real configure-resource or document operation to encode.</param>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task NonemptyRealOperationCannotUseProbe(int index)
    {
        var operation = KeyLoad.CrashHost.ReplicaCrashModel.Operation(index);
        var request = new AppendRequest(ReadRoundStoredCluster.VoterA, 3, 0, 0, 0, [new(1, 3, operation)]);
        await InvalidProbeCannotReachReceiverOrMutateStoredAuthority(Encoding.UTF8.GetString(ReplicaProtocolCodec.Serialize(request)));
    }

    /// <summary>Both read APIs preserve genuine caller cancellation while transport readiness is pending.</summary>
    /// <param name="control">Whether the trusted native read API is exercised.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelledReadDoesNotWaitForTransportOrChangeStoredState(bool control)
    {
        await using var cluster = new ReadRoundStoredCluster(1);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        await cancellation.CancelAsync();
        var node = cluster.Nodes[0];
        await Assert.ThrowsAsync<OperationCanceledException>(() => control
            ? node.Consensus.ReadControlBarrierAsync(cancellation.Token)
            : node.Consensus.ReadBarrierAsync(cancellation.Token));
        await Assert.That(node.Log.State.Term).IsEqualTo(0);
        await Assert.That(node.Log.State.LastIndex).IsEqualTo(0);
        await Assert.That(node.Database.LastApplied).IsEqualTo(0);
    }
}

internal static class ReadRoundGuardPayloads
{
    private const string Header = "\"leaderId\":\"read-round-a\",\"term\":3,\"previousIndex\":0,\"previousTerm\":0,\"committedIndex\":0";
    internal const string Nonempty = "{" + Header + ",\"entries\":[{\"index\":1,\"term\":3,\"operation\":null}]}";
    internal const string NullEntries = "{" + Header + ",\"entries\":null}";
    internal const string MissingEntries = "{" + Header + "}";
    internal const string MissingLeader = "{\"term\":3,\"previousIndex\":0,\"previousTerm\":0,\"committedIndex\":0,\"entries\":[]}";
    internal const string MissingTerm = "{\"leaderId\":\"read-round-a\",\"previousIndex\":0,\"previousTerm\":0,\"committedIndex\":0,\"entries\":[]}";
    internal const string MissingPreviousIndex = "{\"leaderId\":\"read-round-a\",\"term\":3,\"previousTerm\":0,\"committedIndex\":0,\"entries\":[]}";
    internal const string MissingPreviousTerm = "{\"leaderId\":\"read-round-a\",\"term\":3,\"previousIndex\":0,\"committedIndex\":0,\"entries\":[]}";
    internal const string MissingCommittedIndex = "{\"leaderId\":\"read-round-a\",\"term\":3,\"previousIndex\":0,\"previousTerm\":0,\"entries\":[]}";
    internal const string Duplicate = "{" + Header + ",\"entries\":[],\"entries\":[]}";
    internal const string Unknown = "{" + Header + ",\"entries\":[],\"unknown\":true}";
    internal const string CaseMutated = "{\"LeaderId\":\"read-round-a\",\"term\":3,\"previousIndex\":0,\"previousTerm\":0,\"committedIndex\":0,\"entries\":[]}";
    internal const string InvalidTerm = "{\"leaderId\":\"read-round-a\",\"term\":0,\"previousIndex\":0,\"previousTerm\":0,\"committedIndex\":0,\"entries\":[]}";
    internal const string InvalidPrevious = "{\"leaderId\":\"read-round-a\",\"term\":3,\"previousIndex\":1,\"previousTerm\":0,\"committedIndex\":0,\"entries\":[]}";
    internal const string Trailing = "{" + Header + ",\"entries\":[]} true";
    internal const string NonemptyString = "\"nonempty\"";
    internal const string Null = "null";
    internal const string Object = "{}";
    internal const string TrailingString = "\"\" true";
}
