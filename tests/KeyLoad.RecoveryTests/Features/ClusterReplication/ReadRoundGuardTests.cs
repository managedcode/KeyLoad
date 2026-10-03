using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-006: actual stored endpoints reject invalid read messages before protocol mutation.</summary>
internal sealed class ReadRoundGuardTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Invalid probes fail while the real apply/protocol gate is held, leaving durable term/log/cut unchanged.</summary>
    /// <param name="payload">The native append field or semantic boundary to reject.</param>
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
            var bytes = ReadRoundGuardWireFixture.Probe(payload);
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => node.Consensus.HandleAsync(
                ReplicaRpc.ReadProbe, bytes, linked.Token));
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(node.Log.State).IsEqualTo(before);
            await Assert.That(node.Database.LastApplied).IsEqualTo(0);
        }
        finally { node.Materializer.ProtocolGate.Release(); }
    }

    /// <summary>Control payloads must be the serialized empty string before the leader gate can run.</summary>
    /// <param name="payload">The native control root or trailing-byte boundary to reject.</param>
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
            var bytes = ReadRoundGuardWireFixture.Control(payload);
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => node.Consensus.HandleAsync(
                ReplicaRpc.ControlReadBarrier, bytes, linked.Token));
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
        await using var cluster = new ReadRoundStoredCluster(1);
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        cluster.Attach();
        var node = cluster.Nodes[0];
        var operation = node.Database.NormalizeOperation(KeyLoad.CrashHost.ReplicaCrashModel.Operation(index));
        var request = new AppendRequest(ReadRoundStoredCluster.VoterA, 3, 0, 0, 0, [new(1, 3, operation)]);
        await node.Materializer.ProtocolGate.WaitAsync(linked.Token);
        try
        {
            var before = node.Log.State;
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => node.Consensus.HandleAsync(
                ReplicaRpc.ReadProbe, ReplicaProtocolCodec.Serialize(request), linked.Token));
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(node.Log.State).IsEqualTo(before);
            await Assert.That(node.Database.LastApplied).IsEqualTo(0);
        }
        finally { node.Materializer.ProtocolGate.Release(); }
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

// Case labels select authentic native malformed frames; none is runtime JSON.
internal static class ReadRoundGuardPayloads
{
    internal const string Nonempty = "nonempty";
    internal const string NullEntries = "null-entries";
    internal const string MissingEntries = "missing-entries";
    internal const string MissingLeader = "missing-leader";
    internal const string MissingTerm = "missing-term";
    internal const string MissingPreviousIndex = "missing-previous-index";
    internal const string MissingPreviousTerm = "missing-previous-term";
    internal const string MissingCommittedIndex = "missing-committed-index";
    internal const string Duplicate = "duplicate";
    internal const string Unknown = "unknown";
    internal const string CaseMutated = "case-mutated-alias";
    internal const string InvalidTerm = "invalid-term";
    internal const string InvalidPrevious = "invalid-previous";
    internal const string Trailing = "trailing";
    internal const string NonemptyString = "nonempty-string";
    internal const string Null = "null-root";
    internal const string Object = "object-root";
    internal const string TrailingString = "trailing-string";
}
