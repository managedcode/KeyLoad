using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentMutationImageFailureTests
{
    private const string Collection = "orders";
    private const string FirstId = "first";
    private const string SecondId = "second";
    private const string FirstJson = "{\"value\":\"same\"}";
    private const string OtherJson = "{\"value\":\"other\"}";
    private const string ReaderId = "reader";
    private const string RootId = "root";

    [Test]
    public async Task AcDstore005UniqueConflictRetainsBeforeImageAndAllowsFollowingWrite()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection, indexes: [new("by-value", ["/value"], Unique: true)]);
        db.Commit(new PutDocument(Collection, FirstId, FirstJson));
        var failed = Submit(db, new PutDocument(Collection, SecondId, FirstJson));

        await Assert.That(Errors.Code(failed.Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(db.Database.GetDocument(RootId, new(db.Partition, Collection, SecondId))).IsNull();
        await Assert.That(db.Database.GetOutboxStatus(RootId, db.Partition).Head.Tail).IsEqualTo(1);
        db.Commit(new PutDocument(Collection, SecondId, OtherJson));
        await Assert.That(db.Database.GetDocument(RootId, new(db.Partition, Collection, SecondId))!.Json).IsEqualTo(OtherJson);
    }

    [Test]
    public async Task AcDstore005OutboxQuotaRollsBackImagesAndAllowsFollowingWriteAfterFailedBatch()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        var bounded = new DatabaseEngine(db.Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(new()
        {
            MaxOutboxRecords = 1
        }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var failed = Apply(bounded, db, new PutDocument(Collection, FirstId, FirstJson),
            new PutDocument(Collection, SecondId, OtherJson));

        await Assert.That(Errors.Code(failed.Problem)).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(db.Database.GetOutboxStatus(RootId, db.Partition).Head.Tail).IsEqualTo(0);
        await Assert.That(db.Database.GetDocument(RootId, new(db.Partition, Collection, FirstId))).IsNull();
        Apply(bounded, db, new PutDocument(Collection, FirstId, FirstJson)).Get<CommitReceipt>();
        await Assert.That(db.Database.GetOutboxStatus(RootId, db.Partition).Head.Tail).IsEqualTo(1);
    }

    [Test]
    public async Task AcDstore005UnauthorizedWriteDoesNotStageImageAndRootCanStillWrite()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(ReaderId, "tenant",
            [new("database", Collection, Capability.DocumentsRead)], []))).Get<PrincipalRecord>();
        var denied = Submit(db, new PutDocument(Collection, FirstId, FirstJson), ReaderId);

        await Assert.That(Errors.Code(denied.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Database.GetOutboxStatus(RootId, db.Partition).Head.Tail).IsEqualTo(0);
        db.Commit(new PutDocument(Collection, FirstId, FirstJson));
        await Assert.That(db.Database.GetDocument(RootId, new(db.Partition, Collection, FirstId))!.Json).IsEqualTo(FirstJson);
    }

    private static Result<object> Submit(TestDatabase db, Mutation mutation, string principal = RootId)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [mutation]), principal, id);
    }

    private static Result<object> Apply(DatabaseEngine engine, TestDatabase db, params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, db.Partition, [.. mutations]);
        return engine.Apply(engine.CreateCommand(OperationKind.Batch, id, RootId, TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options)));
    }
}
