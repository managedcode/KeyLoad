using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.UnitTests.Features.DatabaseComposition.CompositionReplayFixture;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class DatabaseCompositionReplayTests
{
    [Test]
    public async Task AcComp005SavedCanonicalEndpointFaultRefusesReplayAndOutcomeThenRepairsAcrossTwoColdOwners()
    {
        foreach (var from in new[] { true, false })
        {
            foreach (var component in new[] { 0, 1, 2 })
            {
                var failures = new List<Exception>();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var db = new TestDatabase();
                    await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                        () => QueueGraphReplayFlow.RunAsync(db, from, component), failures);
                }, failures);
                global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            }
        }
    }

    [Test]
    public async Task AcComp005ForwardRetryKeepsOriginalReceiptAfterSourceChanges()
    {
        using var db = NewDatabase();
        db.Commit(new EnqueueMessage(Queue, Message, Link(db, First, Second)));
        var expectedFrom = DatabaseCompositionExpectedReferences.Capture(db, Vertex(db, First));
        var expectedTo = DatabaseCompositionExpectedReferences.Capture(db, Vertex(db, Second));
        var operation = Forward(db);
        var first = db.Database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(first.Mutations.Single().CompositionReferences.SequenceEqual([expectedFrom, expectedTo])).IsTrue();
        db.Commit(new EnqueueMessage(Queue, "new-source", Link(db, Second, Third)));

        var retry = db.Database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(retry.CommandId).IsEqualTo(first.CommandId);
        await Assert.That(retry.Token).IsEqualTo(first.Token);
        await Assert.That(SameEffects(retry, first)).IsTrue();
        await Assert.That(db.Store.Read(view => view.GetRecord<EdgeRecord>(
            KeySpace.Partition("edge", db.Partition, Graph, Prefix + "new-source")))).IsNull();
        var changed = operation with
        {
            PayloadJson = JsonSerializer.Serialize(
            new CommandRequest(operation.Id, db.Partition, [new QueueToGraph(Graph, Queue, "changed-")]),
            JsonDefaults.Options)
        };
        var conflict = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Apply(changed).Get<CommitReceipt>());
        await Assert.That(conflict.Code).IsEqualTo(ErrorCode.Conflict);
    }

    [Test]
    public async Task AcComp005SavedEndpointRejectsRowAccessChangeWithoutPrincipalEpochChange()
    {
        using var db = NewDatabase();
        db.Commit(new EnqueueMessage(Queue, Message, Link(db, First, Second)));
        ConfigureReader(db);
        var operation = Forward(db, Principal);
        db.Database.Apply(operation).Get<CommitReceipt>();
        var epoch = db.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Principal))!.PolicyEpoch);

        db.Commit(new PutDocument(Collection, Second, "{}", ExpectedRevision: 1, Access: new("someone-else")));
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Apply(operation).Get<CommitReceipt>());
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(db.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Principal))!.PolicyEpoch))
            .IsEqualTo(epoch);
        await Assert.That(db.Database.ResolveOutcome(operation).Error).IsEqualTo(ErrorCode.NotFound);
    }

    [Test]
    public async Task AcComp005ReplayRejectsRevokedSourceAndTargetGrants()
    {
        foreach (var revoked in new[] { Capability.QueueInspect, Capability.GraphWrite })
        {
            using var db = NewDatabase();
            db.Commit(new EnqueueMessage(Queue, Message, Link(db, First, Second)));
            ConfigureReader(db);
            var operation = Forward(db, Principal);
            db.Database.Apply(operation).Get<CommitReceipt>();

            ConfigureReader(db, revoked);
            var denied = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Apply(operation).Get<CommitReceipt>());
            await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        }
    }

    [Test]
    public async Task AcComp005OrderedDeleteRetainsSelectedRowAuthority()
    {
        using var db = NewDatabase();
        db.Commit(new EnqueueMessage(Queue, Message, Link(db, First, Second)));
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, db.Partition,
            [new QueueToGraph(Graph, Queue, Prefix), new DeleteEdge(Graph, Prefix + Message)]);
        var operation = new ReplicatedOperation(id, OperationKind.Batch, "root", TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(request, JsonDefaults.Options));

        var first = db.Database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(first.Mutations.Length).IsEqualTo(2);
        await Assert.That(db.Store.Read(view => view.GetRecord<EdgeRecord>(
            KeySpace.Partition("edge", db.Partition, Graph, Prefix + Message)))).IsNull();
        var retry = db.Database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(retry.Token).IsEqualTo(first.Token);
    }

    [Test]
    public async Task AcComp005UnrelatedPrefixMessageDoesNotBecomeCompositionAuthority()
    {
        using var db = NewDatabase();
        db.Commit(new UpsertEdge(Graph, "edge", Vertex(db, First), Vertex(db, Second), Label));
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, db.Partition,
            [new EnqueueMessage(Queue, Prefix + "plain", "{}"),
                new GraphToQueueMutation(Queue, Graph, Vertex(db, First), Prefix)]);
        var operation = new ReplicatedOperation(id, OperationKind.Batch, "root", TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(request, JsonDefaults.Options));

        var receipt = db.Database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(receipt.Mutations.Length).IsEqualTo(2);
        await Assert.That(receipt.Mutations[0].CompositionReferences).IsEmpty();
        await Assert.That(receipt.Mutations[1].CompositionReferences.Length).IsEqualTo(2);
        var native = NativeSerialization.Deserialize<CommitReceipt>(NativeSerialization.Serialize(receipt));
        await Assert.That(native.Mutations[0].CompositionReferences).IsEmpty();
        await Assert.That(native.Mutations[1].CompositionReferences.SequenceEqual(receipt.Mutations[1].CompositionReferences))
            .IsTrue();
        var publicJson = JsonSerializer.Serialize(receipt, JsonDefaults.Options);
        await Assert.That(publicJson.Contains("CompositionReferences", StringComparison.Ordinal))
            .IsFalse();
        await Assert.That(db.Database.Apply(operation).Get<CommitReceipt>().Token).IsEqualTo(receipt.Token);
    }

    [Test]
    public async Task AcComp005ProcessingWrapperRejectsCompositionBeforeLeaseUse()
    {
        using var db = NewDatabase();
        var id = Guid.NewGuid();
        var request = new ProcessingRequest(id, new(db.Partition, Queue), "invalid-lease", "handler", 1,
            [new QueueToGraph(Graph, Queue, Prefix)]);
        var rejected = db.Submit(OperationKind.Processing, request, id: id);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.UnsupportedCapability);
    }

    [Test]
    public async Task AcComp005EmptyReverseSelectionStillChecksSavedStart()
    {
        using var db = NewDatabase();
        var grants = new[]
        {
            new ScopeGrant(db.Partition.DatabaseId, Collection, Capability.DocumentsRead),
            new ScopeGrant(db.Partition.DatabaseId, Graph, Capability.GraphRead),
            new ScopeGrant(db.Partition.DatabaseId, Queue, Capability.QueuePublish)
        };
        var principal = new PrincipalRecord(Principal, db.Partition.TenantId, [.. grants], [])
        {
            RestrictRows = true,
            OwnerId = Principal,
            PolicyEpoch = 1
        };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, db.Partition,
            [new GraphToQueueMutation(Queue, Graph, Vertex(db, First), Prefix)]);
        var operation = new ReplicatedOperation(id, OperationKind.Batch, Principal, TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(request, JsonDefaults.Options));

        var first = db.Database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(first.Mutations).IsEmpty();
        db.Commit(new PutDocument(Collection, First, "{}", ExpectedRevision: 1, Access: new("someone-else")));
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Apply(operation).Get<CommitReceipt>());
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.NotFound);
    }

    [Test]
    public async Task AcComp005PrivateAuthoritySurvivesReopenAndReverseStartRemainsVisible()
    {
        var db = NewDatabase();
        try
        {
            db.Commit(new UpsertEdge(Graph, "edge", Vertex(db, First), Vertex(db, Second), Label));
            var commandId = Guid.NewGuid();
            var request = new CommandRequest(commandId, db.Partition,
                [new GraphToQueueMutation(Queue, Graph, Vertex(db, First), Prefix)]);
            var operation = new ReplicatedOperation(commandId, OperationKind.Batch, "root", TimeProvider.System.GetUtcNow(),
                JsonSerializer.Serialize(request, JsonDefaults.Options));
            var original = db.Database.Apply(operation).Get<CommitReceipt>();
            db.Store.Dispose();
            using var reopened = new ZoneTreeStore(new(db.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var engine = new DatabaseEngine(reopened, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);

            var retry = engine.Apply(operation).Get<CommitReceipt>();
            var resolved = engine.ResolveOutcome(operation).Get<CommitReceipt>();
            await Assert.That(retry.Token).IsEqualTo(original.Token);
            await Assert.That(SameEffects(retry, original)).IsTrue();
            await Assert.That(resolved.Token).IsEqualTo(original.Token);
            await Assert.That(SameEffects(resolved, original)).IsTrue();
        }
        finally
        {
            db.Dispose();
        }
    }

}
