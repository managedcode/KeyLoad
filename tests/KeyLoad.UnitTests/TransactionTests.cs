using KeyLoad.Query;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

public sealed class TransactionTests
{
    [Fact]
    public void DocumentEventAndQueueCommitTogetherAndCommandRetryDoesNotRepeatEffects()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, indexes: [new("number", ["/number"], true)]);
        db.Configure("events", ResourceKind.StreamSet);
        db.Configure("jobs", ResourceKind.WorkQueue);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, db.Partition, [new PutDocument("orders", "o1", "{\"number\":1}", 0),
            new AppendEvents("events", "o1", [new("e1", "Created", "{\"number\":1}")], ExpectedStreamRevision.NoStream),
            new EnqueueMessage("jobs", "m1", "{\"order\":\"o1\"}")]);
        var first = db.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        var retry = db.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        Assert.Equal(first.Token, retry.Token);
        Assert.Equal(1, db.Database.GetDocument("root", new(db.Partition, "orders", "o1"))!.Revision);
        Assert.Single(db.Database.ReadStream("root", new(db.Partition, "events", "o1")).Events);
        Assert.Equal(MessageState.Ready, db.Database.InspectMessage("root", new(db.Partition, "jobs"), "m1")!.Metadata.State);
        Assert.Equal(ErrorCode.Conflict, db.Submit(OperationKind.Batch, command with { Mutations = [new PutDocument("orders", "o2", "{}") ] }, id: id).Error);
    }
    [Fact]
    public void UniqueConflictRollsBackDocumentIndexEventAndEnqueue()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, indexes: [new("number", ["/number"], true)]);
        db.Configure("events", ResourceKind.StreamSet); db.Configure("jobs", ResourceKind.WorkQueue);
        db.Commit(new PutDocument("orders", "existing", "{\"number\":1}"));
        var id = Guid.NewGuid();
        var result = db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition,
            [new AppendEvents("events", "o2", [new("e2", "Created", "{}")], ExpectedStreamRevision.NoStream),
            new EnqueueMessage("jobs", "m2", "{}"), new PutDocument("orders", "o2", "{\"number\":1}")]), id: id);
        Assert.Equal(ErrorCode.Conflict, result.Error);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "orders", "o2")));
        Assert.Empty(db.Database.ReadStream("root", new(db.Partition, "events", "o2")).Events);
        Assert.Null(db.Database.InspectMessage("root", new(db.Partition, "jobs"), "m2"));
        Assert.Single(new QueryEngine(db.Database).Execute("root", new(db.Partition, "SELECT * FROM orders WHERE number = 1")).Rows);
    }
    [Fact]
    public async Task ConcurrentCompareAndSwapHasOneWinner()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "o1", "{}"));
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(number => Task.Run(() =>
        {
            var id = Guid.NewGuid();
            return db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [new PutDocument("orders", "o1", $"{{\"winner\":{number}}}", 1)]), id: id);
        })));
        Assert.Single(results, r => r.Error is null);
        Assert.Equal(2, db.Database.GetDocument("root", new(db.Partition, "orders", "o1"))!.Revision);
    }
    [Fact]
    public void SameLiteralPartitionKeyCannotCrossTransactionDomains()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("other", ResourceKind.WorkQueue, domain: "another-domain");
        var id = Guid.NewGuid();
        Assert.Equal(ErrorCode.Conflict, db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition,
            [new PutDocument("orders", "o1", "{}"), new EnqueueMessage("other", "m1", "{}")]), id: id).Error);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "orders", "o1")));
    }
    [Theory]
    [InlineData("mutation")]
    [InlineData("patch")]
    [InlineData("event")]
    [InlineData("topic")]
    [InlineData("sample")]
    [InlineData("patch-enum")]
    [InlineData("revision-enum")]
    public void MalformedMutationElementsBecomePersistedRejectionsRatherThanApplyExceptions(string shape)
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("events", ResourceKind.StreamSet);
        db.Configure("topics", ResourceKind.Topic); db.Configure("samples", ResourceKind.TimeSeries);
        Mutation mutation = shape switch
        {
            "mutation" => null!, "patch" => new PatchDocument("orders", "a", [null!], 1),
            "event" => new AppendEvents("events", "a", [null!], ExpectedStreamRevision.NoStream),
            "topic" => new PublishTopic("topics", [null!]), "sample" => new AppendSamples("samples", "a", [null!]),
            "patch-enum" => new PatchDocument("orders", "a", [new("/field", (PatchKind)99)], 1),
            _ => new AppendEvents("events", "a", [new("e", "Created", "{}")], new((ExpectedStreamState)99))
        };
        var id = Guid.NewGuid(); var result = db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [mutation]), id: id);
        Assert.Equal(ErrorCode.Validation, result.Error); Assert.Equal(ErrorCode.Validation, db.Database.Outcome("root", id)!.Error);
        Assert.Equal(0, db.Database.GetOutboxStatus("root", db.Partition).Head.Tail);
        db.Commit(new PutDocument("orders", "valid-after-rejection", "{}", 0));
        Assert.NotNull(db.Database.GetDocument("root", new(db.Partition, "orders", "valid-after-rejection")));
    }
    [Fact]
    public void OversizedCompiledFramePersistsItsFailureAndAdvancesTheRaftApplyPosition()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-frame-budget-" + Guid.NewGuid().ToString("N"));
        try
        {
            Guid rejectedId;
            using (var store = new ZoneTreeStore(new(root) { MaxFrameBytes = 4_096 }))
            {
                var database = new DatabaseEngine(store, new AuthorizationPolicy());
                database.Bootstrap(new("root", "system", [new("*", "*", Capability.All)], ["*"]) { ClusterAdministrator = true },
                    DatabaseEngine.Credential("root", "root", "root.frame-test-credential-32-characters"));
                OperationResult Submit<T>(OperationKind kind, Guid id, T payload, long index) => database.Apply(new(id, kind, "root", DateTimeOffset.UtcNow,
                    JsonSerializer.Serialize(payload, JsonDefaults.Options)), index);
                var partition = new PartitionRef("tenant", "database", "orders", "partition");
                Submit(OperationKind.ConfigureResource, Guid.NewGuid(), new ConfigureResourceRequest("tenant", "database", new("orders", ResourceKind.Collection, "orders")), 1).Get<ResourceDefinition>();
                rejectedId = Guid.NewGuid(); var rejected = new CommandRequest(rejectedId, partition,
                    [new PutDocument("orders", "too-big", "{\"data\":\"" + new string('x', 2_000) + "\"}", 0)]);
                Assert.Equal(ErrorCode.ResourceExhausted, Submit(OperationKind.Batch, rejectedId, rejected, 2).Error);
                Assert.Equal(2, database.LastApplied); Assert.Equal(0, database.GetOutboxStatus("root", partition).Head.Tail);
                Assert.Null(database.GetDocument("root", new(partition, "orders", "too-big")));
                Assert.Equal(ErrorCode.ResourceExhausted, Submit(OperationKind.Batch, rejectedId, rejected, 3).Error);
                var nextId = Guid.NewGuid(); Submit(OperationKind.Batch, nextId, new CommandRequest(nextId, partition, [new PutDocument("orders", "valid", "{}", 0)]), 4).Get<CommitReceipt>();
                Assert.Equal(4, database.LastApplied); Assert.Equal(1, database.GetOutboxStatus("root", partition).Head.Tail);
            }
            using var reopened = new ZoneTreeStore(new(root) { MaxFrameBytes = 4_096 }); var recovered = new DatabaseEngine(reopened, new AuthorizationPolicy());
            Assert.Equal(4, recovered.LastApplied); Assert.Equal(ErrorCode.ResourceExhausted, recovered.Outcome("root", rejectedId)!.Error);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void InvalidCatalogArraysGrantsAndVerifiersCannotPoisonReplicatedAuthorization()
    {
        using var db = new TestDatabase(); var definition = new ResourceDefinition("invalid", ResourceKind.Collection, db.Partition.TransactionDomainId);
        foreach (var invalid in new[] { definition with { Indexes = [null!] }, definition with { FieldPolicies = [null!] },
            definition with { HeaderPolicies = [null!] }, definition with { Indexes = [new("field", [null!])] }, definition with { Kind = (ResourceKind)99 } })
            Assert.Equal(ErrorCode.Validation, db.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest("tenant", "database", invalid)).Error);
        foreach (var grants in new ScopeGrant[][] { [null!], [new("database", "orders", (Capability)(1L << 50))] })
            Assert.Equal(ErrorCode.Validation, db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("invalid", "tenant", grants, []))).Error);
        Assert.Equal(ErrorCode.Validation, db.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(new("invalid", "root", "invalid-verifier"))).Error);
        Assert.Equal("root", db.Database.Authenticate("root.unit-test-credential-32-characters", DateTimeOffset.UtcNow));
        db.Configure("orders", ResourceKind.Collection); db.Commit(new PutDocument("orders", "valid", "{}", 0));
    }
    [Fact]
    public void AShadowedMutationResourceCannotAuthorizeWritesToAnotherCollection()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("other", ResourceKind.Collection);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("writer", "tenant", [new("database", "other", Capability.DocumentsWrite)], []))).Get<PrincipalRecord>();
        var id = Guid.NewGuid(); var attack = new PutDocument("orders", "unauthorized", "{}", 0) { Resource = "other" };
        Assert.Equal(ErrorCode.Validation, db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [attack]), "writer", id).Error);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "orders", "unauthorized")));
        var allowedId = Guid.NewGuid(); db.Submit(OperationKind.Batch, new CommandRequest(allowedId, db.Partition, [new PutDocument("other", "allowed", "{}", 0)]), "writer", allowedId).Get<CommitReceipt>();
        Assert.NotNull(db.Database.GetDocument("root", new(db.Partition, "other", "allowed")));
    }
}
