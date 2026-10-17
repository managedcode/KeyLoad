using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardUncertainProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardUncertainFlow
{
    internal static async Task RunAsync(bool timeout, CancellationToken token)
    {
        var observation = new ReplicaForwardUncertainObservation(timeout);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new RequestCqrsHostedRuntimeFixture(observer: observation);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync(token);
                await ExecuteAsync(fixture, observation, token);
            }, failures);
            observation.End();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(RequestCqrsHostedRuntimeFixture fixture,
        ReplicaForwardUncertainObservation observation, CancellationToken token)
    {
        var definition = fixture.Database.Configure(P.Collection, ResourceKind.Collection);
        var partition = fixture.Database.Partition;
        var lifetime = fixture.Database.Database.Store.Read(view => ResourceLifetimeCatalog.Read(view,
            partition.TenantId, partition.DatabaseId, P.Collection, definition))
            ?? throw new InvalidOperationException(P.Missing);
        var nodes = ReplicaForwardedOriginalWorkNodes.All(fixture);
        await Assert.That(nodes.Length).IsEqualTo(P.Voters);
        await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
        var leader = await ReplicaForwardedOriginalWorkNodes.LeaderAsync(nodes, token);
        var follower = nodes.First(node => !ReferenceEquals(node, leader));
        await Assert.That((await follower.Journal.Consensus.StateAsync(token)).Role).IsNotEqualTo(ReplicaRole.Leader);
        var work = Budget(follower, fixture.Clock, token);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(P.Collection, P.Lost, P.Json, P.Zero)]);
        var original = follower.Journal.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId,
            P.Root, fixture.Clock.GetUtcNow(), NativeSerialization.Serialize(command), work);
        var positions = nodes.Select(node => node.Journal.Database.Store.Position).ToArray();
        var index = checked(leader.Journal.Log.State.LastIndex + P.One);
        observation.Arm(original.Id, work);
        var failure = await Assert.ThrowsAsync<KeyLoadException>(() =>
            follower.Hosted!.Coordinator.SubmitVerifiedAsync(original, token, work));
        var actual = failure ?? throw new InvalidOperationException(P.Missing);
        await ReplicaForwardUncertainAssertions.FailureAsync(observation, original, work, actual, token);
        observation.End();
        await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
        var reference = new EntityRef(partition, P.Collection, P.Lost)
        {
            Lifetime = new EntityRefLifetimeV1(lifetime.Incarnation.Value)
        };
        var expected = new CommitReceipt(original.Id,
            new(leader.Journal.Database.Store.Identity.Incarnation, partition.AtomicPartitionId,
                index, P.InitialRevision),
            [new(P.PutKind, P.Collection, P.Lost, P.InitialRevision)], DurabilityProfile.QuorumProcessDurable);
        await ReplicaForwardUncertainAssertions.StateAsync(nodes, reference, expected, original, P.One, token);
        await ReplicaForwardUncertainAssertions.SingleCommitAsync(nodes, positions, index);
        await ReplicaForwardUncertainAssertions.PublishedAsync(observation, expected);
        await ReplicaForwardUncertainContinuation.RunAsync(fixture, nodes, follower, observation,
            original, reference, expected, work, actual, token);
    }

    internal static ReadExecutionBudget Budget(RequestCqrsCohortNativeNode node, TimeProvider clock,
        CancellationToken token)
    {
        var work = new ReadExecutionBudget(node.Journal.Database.OperationLimitsOptions, clock, token);
        work.ConstrainNativeWork(work.MaximumNativeReadBytes, work.MaximumNativeScanRecords);
        return work;
    }
}
