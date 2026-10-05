using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class TransactionProtocolTests
{
    private const string PartitionId = "partition";

    [Test]
    [Arguments("mutation")]
    [Arguments("patch")]
    [Arguments("event")]
    [Arguments("topic")]
    [Arguments("sample")]
    [Arguments("patch-enum")]
    [Arguments("revision-enum")]
    public async Task MalformedMutationElementsBecomePersistedRejectionsRatherThanApplyExceptions(string shape)
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("events", ResourceKind.StreamSet);
        db.Configure("topics", ResourceKind.Topic);
        db.Configure("samples", ResourceKind.TimeSeries);
        Mutation mutation = shape switch
        {
            "mutation" => null!,
            "patch" => new PatchDocument("orders", "a", [null!], 1),
            "event" => new AppendEvents("events", "a", [null!], ExpectedStreamRevision.NoStream),
            "topic" => new PublishTopic("topics", [null!]),
            "sample" => new AppendSamples("samples", "a", [null!]),
            "patch-enum" => new PatchDocument("orders", "a", [new("/field", (PatchKind)99)], 1),
            _ => new AppendEvents("events", "a", [new("e", "Created", "{}")], new((ExpectedStreamState)99))
        };
        var id = Guid.NewGuid();
        var result = db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [mutation]), id: id);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(db.Database.Outcome("root", id)!.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(0);
        db.Commit(new PutDocument("orders", "valid-after-rejection", "{}", 0));
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "valid-after-rejection"))).IsNotNull();
    }
    [Test]
    public async Task OversizedCompiledFramePersistsItsFailureAndAdvancesTheRaftApplyPosition()
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
                PhysicalShardTestBootstrap.Bootstrap(database, "root");
                OperationResult Submit<T>(OperationKind kind, Guid id, T payload, long index) => database.Apply(new(id, kind, "root", TimeProvider.System.GetUtcNow(),
                    JsonSerializer.Serialize(payload, JsonDefaults.Options)), index);
                var partition = new PartitionRef("tenant", "database", "orders", PartitionId);
                Submit(OperationKind.ConfigureResource, Guid.NewGuid(), new ConfigureResourceRequest("tenant", "database", new("orders", ResourceKind.Collection, "orders")), 1).Get<ResourceDefinition>();
                rejectedId = Guid.NewGuid();
                var rejected = new CommandRequest(rejectedId, partition,
                    [new PutDocument("orders", "too-big", "{\"data\":\"" + new string('x', 2_000) + "\"}", 0)]);
                await Assert.That(Submit(OperationKind.Batch, rejectedId, rejected, 2).Error).IsEqualTo(ErrorCode.ResourceExhausted);
                await Assert.That(database.LastApplied).IsEqualTo(2);
                await Assert.That(database.GetOutboxStatus("root", partition).Head.Tail).IsEqualTo(0);
                await Assert.That(database.GetDocument("root", new(partition, "orders", "too-big"))).IsNull();
                await Assert.That(Submit(OperationKind.Batch, rejectedId, rejected, 3).Error).IsEqualTo(ErrorCode.ResourceExhausted);
                var nextId = Guid.NewGuid();
                Submit(OperationKind.Batch, nextId, new CommandRequest(nextId, partition, [new PutDocument("orders", "valid", "{}", 0)]), 4).Get<CommitReceipt>();
                await Assert.That(database.LastApplied).IsEqualTo(4);
                await Assert.That(database.GetOutboxStatus("root", partition).Head.Tail).IsEqualTo(1);
            }
            using var reopened = new ZoneTreeStore(new(root) { MaxFrameBytes = 4_096 });
            var recovered = new DatabaseEngine(reopened, new AuthorizationPolicy());
            PhysicalShardTestBootstrap.RequireExisting(recovered);
            await Assert.That(recovered.LastApplied).IsEqualTo(4);
            await Assert.That(recovered.Outcome("root", rejectedId)!.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
