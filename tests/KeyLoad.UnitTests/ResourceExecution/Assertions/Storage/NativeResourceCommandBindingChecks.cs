using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterReplication;
using KeyLoad.UnitTests.Features.ClusterRouting;
using A = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeAssertions;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeResourceCommandBindingChecks
{
    internal const int NoEntries = 0;
    private const int InitialEpoch = 1;
    private const int RepairedEpoch = 2;
    private const int FirstRevision = 1;
    private const int InitialRevision = 0;
    private const int NextIndex = 1;
    private const int OneLifetime = 1;
    private const int BytesBelow = 1;
    private const string Subject = "native-binding-reader";
    private const string Document = "native-binding-healthy";
    private const string Json = "{\"value\":\"literal-bound-command\"}";
    private const string PermissionDetail = "The principal cannot perform this operation in this scope.";
    private const string OldPolicyDetail = "The principal policy changed since this command was evaluated.";
    private const string ResultDetail = "The read result byte budget is exceeded.";

    internal static async Task<(ReplicatedOperation Operation, CommitReceipt Receipt)> CommandsAsync(
        RequestCqrsCohortRuntimeFixture fixture, NativeLocalChildLifetimeSeeded state, ClusterCoordinator queue,
        NativeRequestWorkLease lease, ResourceLifetimeWorkResultRetention ledger, CancellationToken token)
    {
        await ConfigureAsync(fixture, state, queue, InitialEpoch, Capability.DocumentsRead, token);
        var denied = Batch(state.Leader.Journal.Database);
        await Assert.That(await state.Leader.Journal.Consensus.IsLeaderAsync(token)).IsTrue();
        var refusal = await state.Leader.Journal.Consensus.SubmitAsync(denied, lease, null, token);
        await A.EqualAsync(new OperationResult(null, ErrorCode.PermissionDenied, PermissionDetail), refusal);
        await Assert.That(ledger.RetainedEntries).IsEqualTo(NoEntries);
        await Assert.That(ledger.RetainedEncodedBytes).IsEqualTo((long)NoEntries);
        await A.RowsAsync(state.Leader, token);
        await ConfigureAsync(fixture, state, queue, RepairedEpoch,
            Capability.DocumentsRead | Capability.DocumentsWrite, token);
        await A.EqualAsync(new OperationResult(null, ErrorCode.PermissionDenied, OldPolicyDetail),
            await queue.SubmitVerifiedAsync(denied, token));
        var operation = Batch(state.Leader.Journal.Database);
        var index = checked(state.Leader.Journal.Log.State.LastIndex + NextIndex);
        await Assert.That(await state.Leader.Journal.Consensus.IsLeaderAsync(token)).IsTrue();
        var result = await state.Leader.Journal.Consensus.SubmitAsync(operation, lease, null, token);
        var expected = new CommitReceipt(operation.Id,
            new(state.PhysicalOwner.Incarnation, P.Partition.AtomicPartitionId, index, state.PhysicalOwner.PlacementEpoch),
            [new(K.MutationKind, P.Collection, Document, FirstRevision)], DurabilityProfile.QuorumProcessDurable);
        var expectedResult = new OperationResult(null)
        {
            NativeValue = expected
        };
        await A.EqualAsync(expectedResult, result);
        await EntryAsync(state, operation, index);
        await RowsAsync(fixture, token);
        return (operation, expected);
    }

    private static async Task ConfigureAsync(RequestCqrsCohortRuntimeFixture fixture,
        NativeLocalChildLifetimeSeeded state, ClusterCoordinator queue, long epoch, Capability capabilities,
        CancellationToken token)
    {
        var expected = new PrincipalRecord(Subject, P.Partition.TenantId,
            [new(P.Partition.DatabaseId, P.Collection, capabilities)], [])
        {
            PolicyEpoch = epoch
        };
        var database = state.Leader.Journal.Database;
        var operation = database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), P.Root,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(expected)));
        var index = checked(state.Leader.Journal.Log.State.LastIndex + NextIndex);
        var actual = await queue.SubmitVerifiedAsync(operation, token);
        await Assert.That(actual.Error).IsNull();
        await A.EqualAsync(expected, actual.Get<PrincipalRecord>());
        await EntryAsync(state, operation, index);
        await A.EqualAsync(actual, await queue.SubmitVerifiedAsync(operation, token));
        foreach (var voter in K.Voters)
        {
            var node = fixture.Node(voter);
            await node.Journal.Consensus.ReadBarrierAsync(token);
            await A.EqualAsync(expected, node.Journal.Database.Store.Read(view =>
                node.Journal.Database.Principal(view, Subject, database.EvaluationClock.GetUtcNow())));
        }
    }

    internal static async Task ReadsAsync(RequestCqrsCohortRuntimeFixture fixture, NativeLocalChildLifetimeSeeded state,
        NativeRequestWorkLease lease, ResourceLifetimeWorkResultRetention ledger, CancellationToken token)
    {
        var database = state.Leader.Journal.Database;
        var lifetime = database.Store.Read(view => ResourceLifetimeCatalog.Read(view, P.Partition.TenantId,
            P.Partition.DatabaseId, P.Collection, database.Resource(view, P.Partition, P.Collection)))
            ?? throw new InvalidOperationException(K.Missing);
        var bytes = NativeSerialization.Measure(lifetime);
        await Assert.That(ledger.RetainedEntries).IsEqualTo(OneLifetime);
        await Assert.That(ledger.RetainedEncodedBytes).IsEqualTo(bytes);
        await Assert.That(lease.Dependencies.SetEquals([lifetime])).IsTrue();
        var images = K.Voters.ToDictionary(voter => voter,
            voter => ReplicaBarrierDeadlineAssertions.Image(fixture.Node(voter)), StringComparer.Ordinal);
        var deniedWork = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => database.GetDocumentBound(Subject,
            new GetDocumentRequest(new(P.Partition, NativeReplicaCommandLeaseProtocol.Other, Document)), deniedWork, lease));
        await Assert.That(denied!.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.Message).IsEqualTo(PermissionDetail);
        var tighter = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
        tighter.AttachResourceLifetimeBinding(lease);
        tighter.ConstrainResultBytes(checked((int)bytes - BytesBelow));
        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => database.GetDocumentBound(Subject,
            new GetDocumentRequest(new(P.Partition, P.Collection, Document)), tighter, lease));
        await Assert.That(exhausted!.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(exhausted.Message).IsEqualTo(ResultDetail);
        await Assert.That(ledger.RetainedEntries).IsEqualTo(OneLifetime);
        await Assert.That(ledger.RetainedEncodedBytes).IsEqualTo(bytes);
        var healthy = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
        await A.EqualAsync(ExpectedDocument(), database.GetDocumentBound(Subject,
            new GetDocumentRequest(new(P.Partition, P.Collection, Document)), healthy, lease));
        foreach (var voter in K.Voters)
        {
            await ReplicaBarrierDeadlineAssertions.UnchangedAsync(fixture.Node(voter), images[voter]);
        }
    }

    internal static async Task ReplayAsync(RequestCqrsCohortRuntimeFixture fixture, NativeLocalChildLifetimeSeeded state,
        ClusterCoordinator queue, ReplicatedOperation operation, CommitReceipt receipt, CancellationToken token)
    {
        await A.EqualAsync(receipt, (await queue.SubmitVerifiedAsync(operation, token)).Get<CommitReceipt>());
        await A.EqualAsync(state.Receipt, (await queue.SubmitVerifiedAsync(state.Write, token)).Get<CommitReceipt>());
        await RowsAsync(fixture, token);
    }

    private static ReplicatedOperation Batch(DatabaseEngine database)
    {
        var id = Guid.NewGuid();
        return database.CreateNativeOperation(OperationKind.Batch, id, Subject, database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(new CommandRequest(id, P.Partition,
                [new PutDocument(P.Collection, Document, Json, InitialRevision)])));
    }

    private static async Task EntryAsync(NativeLocalChildLifetimeSeeded state, ReplicatedOperation operation, long index)
    {
        var log = state.Leader.Journal.Log;
        var entry = log.ReadEntry(index) ?? throw new InvalidOperationException(K.Missing);
        await Assert.That(log.State.CommittedIndex >= index).IsTrue();
        var committed = entry.Operation ?? throw new InvalidOperationException(K.Missing);
        await Assert.That(committed.Id).IsEqualTo(operation.Id);
        await Assert.That(committed.PrincipalId).IsEqualTo(operation.PrincipalId);
        await Assert.That(committed.Kind).IsEqualTo(operation.Kind);
        await Assert.That(committed.NativePayload.Span.SequenceEqual(operation.NativePayload.Span)).IsTrue();
    }

    private static async Task RowsAsync(RequestCqrsCohortRuntimeFixture fixture, CancellationToken token)
    {
        foreach (var voter in K.Voters)
        {
            var node = fixture.Node(voter);
            await node.Journal.Consensus.ReadBarrierAsync(token);
            await A.RowsAsync(node, token);
            await A.EqualAsync(ExpectedDocument(), node.Journal.Database.GetDocument(Subject,
                new(P.Partition, P.Collection, Document), cancellationToken: token));
        }
    }

    private static DocumentResult ExpectedDocument()
        => new(new(P.Partition, P.Collection, Document), FirstRevision, Json, false, []);
}
