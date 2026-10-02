using System.Globalization;
using System.Text;
using KeyLoad.Client;
using KeyLoad.Query;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterReplicationTestSupport
{
    private static readonly CompositeFormat NodeNameFormat = CompositeFormat.Parse("node{0}");

    internal static string NodeName(int number) => string.Format(CultureInfo.InvariantCulture, NodeNameFormat, number);

    internal static T Success<T>(Result<T> result)
    {
        if (!result.IsSuccess)
        {
            Assert.Fail(result.Problem?.Detail ?? "The client operation failed.");
        }

        return result.Value!;
    }

    internal static async Task EventuallyAsync(Func<Task<bool>> predicate, CancellationToken cancellationToken)
    {
        while (!await predicate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken);
        }
    }

    internal static async Task<Result<T>> RetryDuringElectionAsync<T>(Func<Task<Result<T>>> action,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await action();
            if (result.IsSuccess)
            {
                return result;
            }

            await Assert.That(new[] { nameof(ErrorCode.UnknownWriteOutcome), nameof(ErrorCode.OwnershipLost) })
                .Contains(result.Problem?.ErrorCode ?? "MissingProblem");
            await Task.Delay(250, cancellationToken);
        }
    }
}

internal static class LeaderLossRecoveryScenario
{
    private const int NodeCount = 3;
    private const string LeadershipFailure = "leader-election-minority";
    private const string Collection = "orders";
    private const string Materialized = "materialized";

    internal static async Task VerifyMinorityAndRecoveryAsync(ClusterFixture fixture, LeaderLossRunState state,
        long projectionThroughSequence, CancellationToken cancellationToken)
    {
        var surviving = state.Clients[state.Survivors[0]];
        _ = ClusterReplicationTestSupport.Success(await state.Clients[state.Survivors[1]].StatusAsync(cancellationToken));
        await fixture.KillContainerAsync(ClusterReplicationTestSupport.NodeName(state.Survivors[1] + 1),
            LeadershipFailure, cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        var denied = await surviving.CommitAsync(
            new(Guid.NewGuid(), state.Partition, [new PutDocument(Collection, "minority", "{}")]), cancellationToken);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That((await surviving.GetAsync(new(state.Partition, Collection, "o1"), cancellationToken)).IsFailed).IsTrue();
        foreach (var index in Enumerable.Range(0, NodeCount).Where(index => index != state.Survivors[0]))
        {
            await fixture.RestartContainerAsync(ClusterReplicationTestSupport.NodeName(index + 1), cancellationToken);
        }

        await ClusterReplicationTestSupport.EventuallyAsync(
            async () => (await surviving.StatusAsync(cancellationToken)).IsSuccess, cancellationToken);
        await Assert.That(ClusterReplicationTestSupport.Success(
            await surviving.GetAsync(new(state.Partition, Collection, "minority"), cancellationToken))).IsNull();
        await VerifyAllNodesReadyAsync(fixture, state, projectionThroughSequence, cancellationToken);
    }

    private static async Task VerifyAllNodesReadyAsync(ClusterFixture fixture, LeaderLossRunState state,
        long projectionThroughSequence, CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, NodeCount))
        {
            await ClusterReplicationTestSupport.EventuallyAsync(async () =>
            {
                var current = await state.Clients[index].StatusAsync(cancellationToken);
                return current.IsSuccess && current.Value!.RoutingReady;
            }, cancellationToken);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(
                ClusterReplicationTestSupport.NodeName(index + 1), cancellationToken);
            await Assert.That(ClusterReplicationTestSupport.Success(
                await state.Clients[index].SubscriptionStatusAsync(state.Subscription, cancellationToken)).Checkpoint).IsEqualTo(3);
            await Assert.That(ClusterReplicationTestSupport.Success(
                await state.Clients[index].GetAsync(new(state.Partition, Collection, "o1"), cancellationToken))!.Revision).IsEqualTo(3);
            await Assert.That(ClusterReplicationTestSupport.Success(
                await state.Clients[index].OutboxStatusAsync(state.Partition, cancellationToken))
                .Consumers.Single().Checkpoint).IsEqualTo(projectionThroughSequence);
            await Assert.That(ClusterReplicationTestSupport.Success(
                await state.Clients[index].GetAsync(new(state.Partition, Materialized, "o1"), cancellationToken))!.Revision).IsEqualTo(1);
        }
    }
}

internal sealed record LeaderLossRunState(KeyLoadClient[] Clients, PartitionRef Partition,
    ProjectionConsumerRef ProjectionConsumer, SubscriptionRef Subscription, CommandRequest Command,
    CommitReceipt Receipt, AstQueryRequest LiveRequest, LiveQuerySnapshot LiveSnapshot, int[] Survivors);
