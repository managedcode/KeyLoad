using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardMetadataProtocol;
using W = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardedOriginalWorkProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardMetadataFlow
{
    internal static async Task RunAsync(bool removeGrant, bool exhaustReceiver, CancellationToken token)
    {
        var observation = new ReplicaForwardMetadataObservation();
        var fixture = new RequestCqrsHostedRuntimeFixture(observer: observation);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await fixture.InitializeAsync(token);
            var nodes = ReplicaForwardedOriginalWorkNodes.All(fixture);
            var definition = fixture.Database.Configure(W.Collection, ResourceKind.Collection);
            var partition = fixture.Database.Partition;
            var lifetime = fixture.Database.Database.Store.Read(view => ResourceLifetimeCatalog.Read(view,
                partition.TenantId, partition.DatabaseId, W.Collection, definition))
                ?? throw new InvalidOperationException(P.Missing);
            var expectedReference = new EntityRef(partition, W.Collection, W.Healthy)
            {
                Lifetime = new EntityRefLifetimeV1(lifetime.Incarnation.Value)
            };
            fixture.Database.Commit(new PutDocument(W.Collection, W.Anchor, W.AnchorJson, P.NewDocument));
            var writer = new PrincipalRecord(P.Writer, fixture.Database.Partition.TenantId,
                [new ScopeGrant(fixture.Database.Partition.DatabaseId, W.Collection, Capability.DocumentsWrite)], []);
            Configure(fixture, writer);
            await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
            var leader = await ReplicaForwardedOriginalWorkNodes.LeaderAsync(nodes, token);
            var follower = nodes.First(node => !ReferenceEquals(node, leader));
            var work = CreateBudget(follower, fixture.Clock, token);
            var command = new CommandRequest(Guid.NewGuid(), fixture.Database.Partition,
                [new PutDocument(W.Collection, W.Healthy, W.HealthyJson, P.NewDocument)]);
            var original = follower.Journal.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId,
                writer.Id, fixture.Clock.GetUtcNow(), NativeSerialization.Serialize(command), work);
            if (removeGrant)
            {
                await RefusedAsync(fixture, nodes, follower, writer, original, work, observation, token);
                command = command with
                {
                    CommandId = Guid.NewGuid()
                };
                original = follower.Journal.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId,
                    writer.Id, fixture.Clock.GetUtcNow(), NativeSerialization.Serialize(command), work);
            }
            if (exhaustReceiver)
            {
                await BudgetRefusedAsync(fixture, nodes, follower, original, work, observation, token);
                work = CreateBudget(follower, fixture.Clock, token);
                command = command with
                {
                    CommandId = Guid.NewGuid()
                };
                original = follower.Journal.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId,
                    writer.Id, fixture.Clock.GetUtcNow(), NativeSerialization.Serialize(command), work);
            }
            await ReplicaForwardMetadataSuccess.RunAsync(fixture, nodes, leader, follower,
                original, command, expectedReference, work, observation, token);
        }, failures);
        observation.End();
        await ServerFailureObserver.ObserveAsync(() => fixture.DisposeAsync().AsTask(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static ReadExecutionBudget CreateBudget(RequestCqrsCohortNativeNode follower,
        TimeProvider clock, CancellationToken token)
    {
        var work = new ReadExecutionBudget(follower.Journal.Database.OperationLimitsOptions, clock, token);
        work.ConstrainNativeWork(work.MaximumNativeReadBytes, work.MaximumNativeScanRecords);
        return work;
    }

    private static async Task BudgetRefusedAsync(RequestCqrsHostedRuntimeFixture fixture,
        RequestCqrsCohortNativeNode[] nodes, RequestCqrsCohortNativeNode follower, ReplicatedOperation original,
        ReadExecutionBudget work, ReplicaForwardMetadataObservation observation, CancellationToken token)
    {
        work.ConstrainNativeWork(work.MaximumNativeReadBytes,
            checked(work.ConstrainedNativeRecords + P.SourceAndReceiverPrincipalReads));
        var images = nodes.Select(ReplicaBarrierDeadlineAssertions.Image).ToArray();
        var cuts = nodes.Select(node => (node.Journal.Log.State.LastIndex, node.Journal.Log.State.CommittedIndex)).ToArray();
        var failure = await Assert.ThrowsAsync<KeyLoadException>(() => SubmitAsync(follower, original, work, observation, token));
        await Assert.That((failure ?? throw new InvalidOperationException(P.Missing)).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var sample = observation.For(work, original.Id);
        await Assert.That(observation.CountFor(original.Id)).IsEqualTo(P.SingleStep);
        await ReplicaForwardMetadataAssertions.ImportedAsync(sample, work, token);
        await Assert.That(((await sample.Lease.Settlement).OriginalFailure as KeyLoadException)?.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(work.RemainingReadGrantRecords).IsEqualTo(P.NoRecords);
        await ReplicaForwardMetadataAssertions.UnchangedAsync(nodes, images, cuts);
        foreach (var node in nodes)
        {
            await Assert.That(OutcomeStoreOracle.ReadPartition(node.Journal.Database.Store,
                fixture.Database.Partition, P.Writer, original.Id)).IsNull();
        }
        work.Check();
    }

    private static async Task RefusedAsync(RequestCqrsHostedRuntimeFixture fixture,
        RequestCqrsCohortNativeNode[] nodes, RequestCqrsCohortNativeNode follower, PrincipalRecord writer,
        ReplicatedOperation original, ReadExecutionBudget work, ReplicaForwardMetadataObservation observation,
        CancellationToken token)
    {
        var removed = writer with
        {
            Grants = [],
            PolicyEpoch = writer.PolicyEpoch + P.SingleStep
        };
        Configure(fixture, removed);
        await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
        var positions = nodes.Select(node => node.Journal.Database.Store.Position).ToArray();
        var cuts = nodes.Select(node => (node.Journal.Log.State.LastIndex, node.Journal.Log.State.CommittedIndex)).ToArray();
        var refused = await SubmitAsync(follower, original, work, observation, token);
        await ReplicaForwardMetadataAssertions.ImportedAsync(observation.For(work, original.Id), work, token);
        await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
        await ReplicaForwardMetadataAssertions.RefusedAsync(nodes, fixture.Database.Partition,
            original, refused, positions, cuts, token);
        Configure(fixture, removed with
        {
            Grants = writer.Grants,
            PolicyEpoch = removed.PolicyEpoch + P.SingleStep
        });
        await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
        var images = nodes.Select(ReplicaBarrierDeadlineAssertions.Image).ToArray();
        cuts = nodes.Select(node => (node.Journal.Log.State.LastIndex, node.Journal.Log.State.CommittedIndex)).ToArray();
        var replay = await SubmitAsync(follower, original, work, observation, token);
        await ReplicaForwardMetadataAssertions.ImportedAsync(observation.For(work, original.Id), work, token);
        await ReplicaForwardedOriginalWorkAssertions.EqualAsync(refused, replay);
        await ReplicaForwardMetadataAssertions.UnchangedAsync(nodes, images, cuts);
        work.Check();
    }

    internal static async Task<OperationResult> SubmitAsync(RequestCqrsCohortNativeNode follower,
        ReplicatedOperation original, ReadExecutionBudget work, ReplicaForwardMetadataObservation observation,
        CancellationToken token)
    {
        await Assert.That((await follower.Journal.Consensus.StateAsync(token)).Role).IsNotEqualTo(ReplicaRole.Leader);
        observation.Begin(original.Id, work);
        try
        {
            return await follower.Hosted!.Coordinator.SubmitVerifiedAsync(original, token, work);
        }
        finally
        {
            observation.End();
        }
    }

    private static void Configure(RequestCqrsHostedRuntimeFixture fixture, PrincipalRecord writer)
        => fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(writer)).Get<PrincipalRecord>();
}
