using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardUnverifiedProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardUnverifiedFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var observation = new ReplicaForwardUnverifiedObservation();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new RequestCqrsHostedRuntimeFixture(observer: observation);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync(token);
                var nodes = ReplicaForwardedOriginalWorkNodes.All(fixture);
                var definition = fixture.Database.Configure(P.Collection, ResourceKind.Collection);
                var partition = fixture.Database.Partition;
                var lifetime = fixture.Database.Database.Store.Read(view => ResourceLifetimeCatalog.Read(view,
                    partition.TenantId, partition.DatabaseId, P.Collection, definition)) ?? throw new InvalidOperationException(P.Missing);
                fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(P.Writer,
                    partition.TenantId, [new ScopeGrant(partition.DatabaseId, P.Collection, Capability.DocumentsWrite)], []))).Get<PrincipalRecord>();
                await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
                var leader = await ReplicaForwardedOriginalWorkNodes.LeaderAsync(nodes, token);
                var follower = nodes.First(node => !ReferenceEquals(node, leader));
                var work = CreateWork(follower, fixture.Clock, token);
                var request = new CommandRequest(Guid.NewGuid(), partition,
                    [new PutDocument(P.Collection, P.OriginalDocument, P.OriginalJson, P.NewRecord)]);
                var original = follower.Journal.Database.CreateNativeOperation(OperationKind.Batch, request.CommandId,
                    P.Writer, fixture.Clock.GetUtcNow(), NativeSerialization.Serialize(request), work);
                var expectedIndex = checked(leader.Journal.Log.State.LastIndex + P.One);
                var expiry = work.ExpiresAt;
                var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
                    SubmitAsync(follower, original, work, observation, corrupt: true, token)) ?? throw new InvalidOperationException(P.Missing);
                // Actual ReplicaEnvelopeAuthenticator.VerifyReply rejects the changed signed payload with this cause.
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
                var sample = observation.Sample(work, original.Id);
                await ReplicaForwardUnverifiedAssertions.TerminalAsync(work, failure, sample, observation, original.Id, expiry, token);
                await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
                var expected = Receipt(original.Id, leader, partition, expectedIndex, P.OriginalDocument);
                await ReplicaForwardUnverifiedAssertions.StateAsync(nodes, original, lifetime, expected,
                    P.OriginalDocument, P.OriginalJson, P.One, token);
                await SameParentAsync(nodes, follower, original, work, failure, observation, token);
                await ReplicaForwardUnverifiedContinuation.RunAsync(fixture, nodes, leader, follower,
                    original, lifetime, expected, observation, token);
            }, failures);
            observation.End();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SameParentAsync(RequestCqrsCohortNativeNode[] nodes, RequestCqrsCohortNativeNode follower,
        ReplicatedOperation original, ReadExecutionBudget work, Exception failure,
        ReplicaForwardUnverifiedObservation observation, CancellationToken token)
    {
        var images = nodes.Select(ReplicaBarrierDeadlineAssertions.Image).ToArray();
        var cuts = nodes.Select(node => (node.Journal.Log.State.LastIndex, node.Journal.Log.State.CommittedIndex)).ToArray();
        var bytes = work.ReadBytes;
        var records = work.ConstrainedNativeRecords;
        var check = Assert.ThrowsExactly<KeyLoadException>(work.Check);
        await Assert.That(ReferenceEquals(check, failure)).IsTrue();
        var reserve = Assert.ThrowsExactly<KeyLoadException>(() => work.CreateReadGrant(P.One, P.One));
        await Assert.That(ReferenceEquals(reserve, failure)).IsTrue();
        var replay = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            SubmitAsync(follower, original, work, observation, corrupt: false, token)) ?? throw new InvalidOperationException(P.Missing);
        await Assert.That(ReferenceEquals(replay, failure)).IsTrue();
        await Assert.That(observation.Count(original.Id)).IsEqualTo(P.One);
        await Assert.That(work.ReadBytes).IsEqualTo(bytes);
        await Assert.That(work.ConstrainedNativeRecords).IsEqualTo(records);
        await Assert.That(work.RemainingReadGrantBytes).IsEqualTo(P.NoBytes);
        await Assert.That(work.RemainingReadGrantRecords).IsEqualTo(P.NoRecords);
        await ReplicaForwardMetadataAssertions.UnchangedAsync(nodes, images, cuts);
    }

    internal static ReadExecutionBudget CreateWork(RequestCqrsCohortNativeNode node, TimeProvider clock, CancellationToken token)
    {
        var work = new ReadExecutionBudget(node.Journal.Database.OperationLimitsOptions, clock, token);
        work.ConstrainNativeWork(work.MaximumNativeReadBytes, work.MaximumNativeScanRecords);
        return work;
    }

    internal static async Task<OperationResult> SubmitAsync(RequestCqrsCohortNativeNode follower,
        ReplicatedOperation original, ReadExecutionBudget work, ReplicaForwardUnverifiedObservation observation,
        bool corrupt, CancellationToken token)
    {
        await Assert.That((await follower.Journal.Consensus.StateAsync(token)).Role).IsNotEqualTo(ReplicaRole.Leader);
        observation.Begin(original.Id, work, corrupt);
        try
        {
            return await follower.Hosted!.Coordinator.SubmitVerifiedAsync(original, token, work);
        }
        finally
        {
            observation.End();
        }
    }

    internal static CommitReceipt Receipt(Guid id, RequestCqrsCohortNativeNode leader, PartitionRef partition,
        long position, string documentId) => new(id,
            new(leader.Journal.Database.Store.Identity.Incarnation, partition.AtomicPartitionId, position, P.FirstRevision),
            [new(ReplicaForwardedOriginalWorkProtocol.PutKind, P.Collection, documentId, P.FirstRevision)],
            DurabilityProfile.QuorumProcessDurable);
}
