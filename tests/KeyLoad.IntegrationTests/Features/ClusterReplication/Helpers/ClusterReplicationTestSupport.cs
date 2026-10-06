using System.Globalization;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.Query;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterReplicationTestSupport
{
    internal const string Rf3DiagnosticsFailureKey = "KeyLoad.Rf3DiagnosticsFailure";
    internal const string Rf3ReadinessFailureKey = "KeyLoad.Rf3ReadinessFailure";
    internal const string Rf3RestartFailureKeyPrefix = "KeyLoad.Rf3RestartFailure.";
    private static readonly CompositeFormat NodeNameFormat = CompositeFormat.Parse("node{0}");

    internal static bool IsNonFatalCleanupFailure(Exception error) =>
        error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    internal static string NodeName(int number) => string.Format(CultureInfo.InvariantCulture, NodeNameFormat, number);

    internal static T Success<T>(Result<T> result)
    {
        if (!result.IsSuccess)
        {
            Assert.Fail($"{result.Problem?.ErrorCode ?? "MissingProblem"}: {result.Problem?.Detail ?? "The client operation failed."}");
        }

        return result.Value!;
    }

    internal static async Task EventuallyAsync(Func<Task<bool>> predicate, CancellationToken cancellationToken)
    {
        while (!await predicate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromMilliseconds(250), TimeProvider.System, cancellationToken);
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

            var code = result.Problem?.ErrorCode ?? "MissingProblem";
            await Assert.That(new[] { nameof(ErrorCode.UnknownWriteOutcome), nameof(ErrorCode.OwnershipLost) }).Contains(code)
                .Because($"Unexpected election retry result: {code}; {result.Problem?.Detail ?? "No safe detail."}");
            await Task.Delay(TimeSpan.FromMilliseconds(250), TimeProvider.System, cancellationToken);
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
        long projectionThroughSequence, ISet<string> stoppedContainers, CancellationToken cancellationToken)
    {
        var surviving = state.Clients[state.Survivors[0]];
        _ = ClusterReplicationTestSupport.Success(await state.Clients[state.Survivors[1]].StatusAsync(cancellationToken));
        var stoppedNode = ClusterReplicationTestSupport.NodeName(state.Survivors[1] + 1);
        stoppedContainers.Add(stoppedNode);
        await fixture.KillContainerAsync(stoppedNode,
            LeadershipFailure, cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(3), TimeProvider.System, cancellationToken);
        var denied = await surviving.CommitAsync(
            new(Guid.NewGuid(), state.Partition, [new PutDocument(Collection, "minority", "{}")]), cancellationToken);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That((await surviving.GetAsync(new(state.Partition, Collection, "o1"), cancellationToken)).IsFailed).IsTrue();
        foreach (var index in Enumerable.Range(0, NodeCount).Where(index => index != state.Survivors[0]))
        {
            var node = ClusterReplicationTestSupport.NodeName(index + 1);
            await fixture.RestartContainerAsync(node, cancellationToken);
            stoppedContainers.Remove(node);
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
                ClusterReplicationTestSupport.NodeName(index + 1), WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
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
