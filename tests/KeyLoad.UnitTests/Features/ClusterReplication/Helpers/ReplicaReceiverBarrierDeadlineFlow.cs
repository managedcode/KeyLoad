using KeyLoad.Clustering;
using KeyLoad.Replication;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;
using A = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineAssertions;
using R = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaReceiverBarrierDeadlineAssertions;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaReceiverBarrierDeadlineFlow
{
    internal static async Task RunAsync(bool control, CancellationToken token)
    {
        var scenario = new P(receiverDeadline: true);
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
        await R.SettingsAsync(fixture);
        var index = checked(leader.Journal.Log.State.LastIndex + 1);
        var command = new CommandRequest(Guid.NewGuid(), P.Partition, [new PutDocument(P.Collection, P.DocumentId, P.Json, 0)]);
        var operation = leader.Journal.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId,
            P.Root, TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(command));
        var receipt = (await leader.Journal.Consensus.SubmitAsync(operation, token)).Get<CommitReceipt>();
        await A.ReceiptAsync(leader, command.CommandId, index, receipt);
        foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
        {
            var node = fixture.Node(voter);
            await node.Journal.Consensus.ReadBarrierAsync(token);
            await A.DocumentAsync(node, token);
        }
        var followers = ReplicaBarrierDeadlineFlow.Voters.Where(voter => voter != leader.Journal.Configuration.LocalId)
            .Select(fixture.Node).ToArray();
        await Assert.That(followers.Length).IsEqualTo(2);
        foreach (var node in followers)
        {
            await node.Journal.Consensus.StopAsync(token);
        }
        var before = ReplicaBarrierDeadlineFlow.Voters.Select(voter => A.Image(fixture.Node(voter))).ToArray();
        await InvokeAsync(followers[0], leader, scenario, control, pending, token);
        for (var offset = 0; offset < ReplicaBarrierDeadlineFlow.Voters.Length; offset++)
        {
            var node = fixture.Node(ReplicaBarrierDeadlineFlow.Voters[offset]);
            await A.UnchangedAsync(node, before[offset]);
            await A.DocumentAsync(node, token);
            await A.ReplayAsync(receipt, node.Journal.Database.ResolveOutcome(operation).Get<CommitReceipt>());
            await A.UnchangedAsync(node, before[offset]);
        }
    }

    private static async Task InvokeAsync(RequestCqrsCohortNativeNode sender, RequestCqrsCohortNativeNode leader,
        P scenario, bool control, List<Task> pending, CancellationToken token)
    {
        await Assert.That(sender.Journal.Configuration.LocalId == leader.Journal.Configuration.LocalId).IsFalse();
        var client = sender.Services.GetRequiredService<ReplicaGrainServiceClient>();
        scenario.WatchReceiver(leader.Journal.Configuration.LocalId);
        var started = TimeProvider.System.GetTimestamp();
        var read = client.InvokeAsync(leader.Journal.Configuration.LocalId,
            control ? ReplicaRpc.ControlReadBarrier : ReplicaRpc.ReadBarrier,
            ReplicaProtocolCodec.Serialize(string.Empty), token);
        pending.Add(read);
        await scenario.FirstBarrier.WaitAsync(token);
        var arrived = TimeProvider.System.GetTimestamp();
        await Task.Delay(P.ReceiverCommandTimeout + R.Tolerance, TimeProvider.System, token);
        await Assert.That(TimeProvider.System.GetElapsedTime(arrived) > P.ReceiverCommandTimeout).IsTrue();
        await Assert.That(read.IsCompleted).IsFalse();
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => read);
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.OwnershipLost);
        pending.Remove(read);
        var elapsed = TimeProvider.System.GetElapsedTime(started);
        var deadline = leader.Journal.ExecutionOptions.Value.ReadBarrierTimeout;
        await Assert.That(elapsed >= deadline - R.Tolerance).IsTrue();
        await Assert.That(elapsed < deadline + P.RpcTimeout).IsTrue();
        await Assert.That(token.IsCancellationRequested).IsFalse();
    }

    private static async Task<RequestCqrsCohortNativeNode> LeaderAsync(RequestCqrsCohortRuntimeFixture fixture,
        CancellationToken token)
    {
        while (true)
        {
            foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
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
