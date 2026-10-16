using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaBarrierDeadlineFlow
{
    private static readonly TimeSpan DeadlineTolerance = TimeSpan.FromMilliseconds(250);
    internal static readonly string[] Voters =
        [RequestCqrsCohortScenario.LocalVoter, RequestCqrsCohortScenario.FirstRemote, RequestCqrsCohortScenario.SecondRemote];

    internal static async Task RunAsync(bool control, CancellationToken token)
    {
        var scenario = new P();
        var failures = new List<Exception>();
        var pending = new List<Task>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new RequestCqrsCohortRuntimeFixture(barrierDeadline: scenario);
            try
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await fixture.InitializeAsync();
                    using var deadline = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.StartupBound, TimeProvider.System);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                    await ExerciseAsync(fixture, scenario, control, pending, linked.Token);
                }, failures);
            }
            finally
            {
                scenario.Release();
                foreach (var task in pending)
                {
                    await ServerFailureObserver.ObserveAsync(() => task.WaitAsync(RequestCqrsCohortRuntimeFixture.ShutdownBound), failures);
                }
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(RequestCqrsCohortRuntimeFixture fixture, P scenario,
        bool control, List<Task> pending, CancellationToken token)
    {
        var leader = await LeaderAsync(fixture, token);
        var follower = fixture.Node(Voters.First(voter => voter != leader.Journal.Configuration.LocalId));
        await follower.Journal.Consensus.ReadBarrierAsync(token);
        await ReplicaBarrierDeadlineAssertions.SettingsAsync(fixture);
        var expectedIndex = checked(leader.Journal.Log.State.LastIndex + 1);
        var command = new CommandRequest(Guid.NewGuid(), P.Partition, [new PutDocument(P.Collection, P.DocumentId, P.Json, 0)]);
        var operation = leader.Journal.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId,
            P.Root, TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(command));
        scenario.Arm(leader.Journal.Configuration.LocalId);
        var submitted = leader.Journal.Consensus.SubmitAsync(operation, token);
        pending.Add(submitted);
        await scenario.Flushed.WaitAsync(token);
        await CancelSelectedAsync(follower, scenario, control, token);
        await HeldReadAsync(follower, scenario, control, pending, token);
        var receipt = (await submitted).Get<CommitReceipt>();
        await ReplicaBarrierDeadlineAssertions.ReceiptAsync(leader, operation.Id, expectedIndex, receipt);
        await CompleteStateAsync(fixture, operation, receipt, token);
        await NoQuorumAsync(fixture, leader, control, token);
    }

    private static async Task CancelSelectedAsync(RequestCqrsCohortNativeNode follower, P scenario,
        bool control, CancellationToken token)
    {
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        var cancelled = Barrier(follower, control, selected.Token);
        try
        {
            await scenario.FirstBarrier.WaitAsync(token);
            await selected.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
            await Assert.That(token.IsCancellationRequested).IsFalse();
        }
        finally
        {
            await selected.CancelAsync();
            await cancelled.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static async Task HeldReadAsync(RequestCqrsCohortNativeNode follower, P scenario,
        bool control, List<Task> pending, CancellationToken token)
    {
        var read = Barrier(follower, control, token);
        pending.Add(read);
        await scenario.SecondBarrier.WaitAsync(token);
        var started = TimeProvider.System.GetTimestamp();
        await Task.Delay(P.Hold, TimeProvider.System, token);
        await Assert.That(TimeProvider.System.GetElapsedTime(started) > P.RpcTimeout).IsTrue();
        await Assert.That(read.IsCompleted).IsFalse();
        scenario.Release();
        await read;
        await Assert.That(TimeProvider.System.GetElapsedTime(started) < follower.Journal.ExecutionOptions.Value.ReadBarrierTimeout).IsTrue();
    }

    private static async Task CompleteStateAsync(RequestCqrsCohortRuntimeFixture fixture,
        ReplicatedOperation operation, CommitReceipt receipt, CancellationToken token)
    {
        foreach (var voter in Voters)
        {
            var node = fixture.Node(voter);
            await node.Journal.Consensus.ReadBarrierAsync(token);
            await ReplicaBarrierDeadlineAssertions.DocumentAsync(node, token);
        }
        var leader = await LeaderAsync(fixture, token);
        var replayIndex = leader.Journal.Log.State.LastIndex;
        var replayTerm = leader.Journal.Log.State.Term;
        var before = ReplicaBarrierDeadlineAssertions.Image(leader);
        var replay = (await leader.Journal.Consensus.SubmitAsync(operation, token)).Get<CommitReceipt>();
        await ReplicaBarrierDeadlineAssertions.ReplayAsync(receipt, replay);
        await ReplicaBarrierDeadlineAssertions.ReplayStateAsync(leader, before, operation.Id, replayIndex, replayTerm);
        foreach (var voter in Voters)
        {
            var node = fixture.Node(voter);
            await node.Journal.Consensus.ReadBarrierAsync(token);
            await ReplicaBarrierDeadlineAssertions.DocumentAsync(node, token);
        }
    }

    private static async Task NoQuorumAsync(RequestCqrsCohortRuntimeFixture fixture,
        RequestCqrsCohortNativeNode leader, bool control, CancellationToken token)
    {
        foreach (var voter in Voters.Where(voter => voter != leader.Journal.Configuration.LocalId))
        {
            await fixture.Node(voter).Journal.Consensus.StopAsync(token);
        }
        var before = ReplicaBarrierDeadlineAssertions.Image(leader);
        var started = TimeProvider.System.GetTimestamp();
        if (control)
        {
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Barrier(leader, true, token));
            await Assert.That(error!.Code).IsEqualTo(ErrorCode.OwnershipLost);
        }
        else
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => Barrier(leader, false, token));
        }
        var elapsed = TimeProvider.System.GetElapsedTime(started);
        var deadline = leader.Journal.ExecutionOptions.Value.ReadBarrierTimeout;
        await Assert.That(elapsed >= deadline - DeadlineTolerance).IsTrue();
        await Assert.That(elapsed < deadline + P.RpcTimeout).IsTrue();
        await Assert.That(token.IsCancellationRequested).IsFalse();
        await ReplicaBarrierDeadlineAssertions.UnchangedAsync(leader, before);
    }

    private static Task Barrier(RequestCqrsCohortNativeNode node, bool control, CancellationToken token)
        => control ? node.Journal.Consensus.ReadControlBarrierAsync(token) : node.Journal.Consensus.ReadBarrierAsync(token);

    internal static async Task<RequestCqrsCohortNativeNode> LeaderAsync(RequestCqrsCohortRuntimeFixture fixture,
        CancellationToken token)
    {
        while (true)
        {
            foreach (var voter in Voters)
            {
                var node = fixture.Node(voter);
                if (await node.Journal.Consensus.IsLeaderAsync(token))
                {
                    return node;
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), TimeProvider.System, token);
        }
    }
}
