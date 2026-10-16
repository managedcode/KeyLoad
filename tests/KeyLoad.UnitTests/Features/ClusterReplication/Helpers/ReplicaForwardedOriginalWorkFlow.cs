using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardedOriginalWorkProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardedOriginalWorkFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var observation = new ReplicaForwardedOriginalWorkObservation();
        var failures = new List<Exception>();
        Task? original = null;
        CancellationTokenSource? selected = null;
        var originalObserved = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new RequestCqrsHostedRuntimeFixture(observer: observation);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync(token);
                var nodes = ReplicaForwardedOriginalWorkNodes.All(fixture);
                var leader = await ReplicaForwardedOriginalWorkNodes.LeaderAsync(nodes, token);
                var follower = nodes.First(node => !ReferenceEquals(node, leader));
                var definition = fixture.Database.Configure(P.Collection, ResourceKind.Collection);
                fixture.Database.Commit(new PutDocument(P.Collection, P.Anchor, P.AnchorJson, P.NewRecordVersion));
                await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
                var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                selected = requestCancellation;
                var command = new CommandRequest(Guid.NewGuid(), fixture.Database.Partition,
                    [new PutDocument(P.Collection, P.Cancelled, P.HealthyJson, P.NewRecordVersion)]);
                observation.HeldCommand = command.CommandId;
                var operation = follower.Journal.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId,
                    P.Root, fixture.Clock.GetUtcNow(), NativeSerialization.Serialize(command));
                var submitted = follower.Journal.Consensus.SubmitAsync(operation, requestCancellation.Token);
                original = submitted;
                await CancelAsync(nodes, observation, submitted, requestCancellation, token);
                originalObserved = true;
                await HealthyAsync(fixture, nodes, leader, follower, definition, observation, token);
            }, failures);
            observation.Release();
            if (selected is not null)
            {
                await ServerFailureObserver.ObserveAsync(selected.CancelAsync, failures);
            }
            if (original is not null && !originalObserved)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                    await original.ConfigureAwait(false), failures);
            }
        }, failures);
        ServerFailureObserver.Observe(() => selected?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CancelAsync(RequestCqrsCohortNativeNode[] nodes,
        ReplicaForwardedOriginalWorkObservation observation, Task<OperationResult> submitted,
        CancellationTokenSource selected, CancellationToken token)
    {
        var held = await observation.Entered.WaitAsync(token);
        await ReplicaForwardedOriginalWorkAssertions.SameOriginalAsync(held);
        var before = nodes.Select(ReplicaBarrierDeadlineAssertions.Image).ToArray();
        await selected.CancelAsync();
        await observation.Cancelled.WaitAsync(token);
        await Assert.That(held.Lease.Settlement.IsCompleted).IsFalse();
        await Assert.That(held.Ledger.RetainedEntries).IsEqualTo(P.NoRetainedEntries);
        observation.Release();
        await Assert.ThrowsAsync<OperationCanceledException>(() => submitted);
        await Assert.That(selected.IsCancellationRequested).IsTrue();
        await Assert.That(submitted.IsCanceled).IsTrue();
        await ReplicaForwardedOriginalWorkAssertions.JoinedAsync(held, token);
        for (var index = 0; index < nodes.Length; index++)
        {
            await ReplicaBarrierDeadlineAssertions.UnchangedAsync(nodes[index], before[index]);
        }
    }

    private static async Task HealthyAsync(RequestCqrsHostedRuntimeFixture fixture, RequestCqrsCohortNativeNode[] nodes,
        RequestCqrsCohortNativeNode leader, RequestCqrsCohortNativeNode follower, ResourceDefinition definition,
        ReplicaForwardedOriginalWorkObservation observation, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Database.Partition,
            [new PutDocument(P.Collection, P.Healthy, P.HealthyJson, P.NewRecordVersion)]);
        var expectedIndex = checked(leader.Journal.Log.State.LastIndex + P.NextLogEntry);
        var payload = NativeSerialization.Serialize(command);
        var receipt = (await follower.Hosted!.Coordinator.SubmitNativeAsync(OperationKind.Batch,
            command.CommandId, P.Root, payload, cancellationToken: token)).Get<CommitReceipt>();
        var observed = observation.ForCommand(command.CommandId);
        await ReplicaForwardedOriginalWorkAssertions.SameOriginalAsync(observed);
        await ReplicaForwardedOriginalWorkAssertions.JoinedAsync(observed, token);
        await ReplicaForwardedOriginalWorkAssertions.ReceiptAsync(leader, fixture.Database.Partition,
            command.CommandId, P.Healthy, expectedIndex, receipt);
        await ReplicaForwardedOriginalWorkAssertions.DependencyAsync(observed, leader, fixture.Database.Partition, definition);
        await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
        await ReplicaForwardedOriginalWorkAssertions.DocumentsAsync(nodes, fixture.Database.Partition, token);
        var before = nodes.Select(ReplicaBarrierDeadlineAssertions.Image).ToArray();
        var replay = (await follower.Hosted!.Coordinator.SubmitNativeAsync(OperationKind.Batch,
            command.CommandId, P.Root, payload, cancellationToken: token)).Get<CommitReceipt>();
        await ReplicaForwardedOriginalWorkAssertions.EqualAsync(receipt, replay);
        var replayObserved = observation.ForCommand(command.CommandId);
        await Assert.That(ReferenceEquals(observed.Work, replayObserved.Work)).IsFalse();
        await ReplicaForwardedOriginalWorkAssertions.SameOriginalAsync(replayObserved);
        await ReplicaForwardedOriginalWorkAssertions.JoinedAsync(replayObserved, token);
        await ReplicaForwardedOriginalWorkAssertions.DependencyAsync(replayObserved, leader, fixture.Database.Partition, definition);
        for (var index = 0; index < nodes.Length; index++)
        {
            await ReplicaBarrierDeadlineAssertions.UnchangedAsync(nodes[index], before[index]);
        }
    }
}
