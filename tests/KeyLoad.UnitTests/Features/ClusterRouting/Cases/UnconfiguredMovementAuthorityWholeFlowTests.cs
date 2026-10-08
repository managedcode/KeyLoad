using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class UnconfiguredMovementAuthorityWholeFlowTests
{
    private const string Collection = "authority-cost-documents";
    private const string DocumentId = "one";
    private const string Json = "{\"value\":1}";
    private const int InvalidNativeByte = 255;
    private const long InitialRevision = 1;
    private static readonly string[] Families =
    [
        "partition-move-source-fence-v1", "partition-move-target-stage-v1", "partition-move-target-page-v1",
        "partition-move-active-v1", "partition-move-control-v1", "partition-move-grant-v1",
        "partition-move-grant-index-v1", "partition-move-grant-count-v1", "partition-move-database-grants-v1",
        "partition-move-principal-grants-v1", "partition-move-cleanup-v1", "partition-move-abort-progress-v1",
        "partition-command-control-v1", "partition-move-published-placement-v1",
    ];

    [Test]
    public async Task RetainedAndMalformedAuthorityRejectsStartupAndCurrentCutThenExactHealthyRead()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                database.Configure(Collection, ResourceKind.Collection);
                database.Commit(new PutDocument(Collection, DocumentId, Json));
                var engine = ControlledMovementUnconfiguredEngine.Create(database.Store);
                foreach (var family in Families)
                {
                    var key = KeyCodec.Encode(family, DocumentId);
                    database.Store.Commit((transaction, _) => { transaction.Put(key, [(byte)InvalidNativeByte]); return true; });
                    var image = ControlledPartitionMovementRawImage.Bytes(database.Store);
                    var position = database.Store.Position;
                    var startup = Assert.ThrowsExactly<KeyLoadException>(() => ControlledMovementUnconfiguredEngine.Create(database.Store));
                    var read = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(view =>
                        engine.Resource(view, database.Partition, Collection, ResourceKind.Collection)));
                    await Assert.That(startup.Code).IsEqualTo(ErrorCode.OwnershipLost);
                    await Assert.That(read.Code).IsEqualTo(ErrorCode.OwnershipLost);
                    await Assert.That(database.Store.Position).IsEqualTo(position);
                    await Assert.That(ControlledPartitionMovementRawImage.Bytes(database.Store).SequenceEqual(image)).IsTrue();
                    await UnconfiguredMovementGlobalCommandAssertions.RejectAsync(engine, database);
                    database.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
                    var healthy = engine.GetDocument("root", new(database.Partition, Collection, DocumentId));
                    await Assert.That(healthy).IsNotNull();
                    var expected = new DocumentResult(new(database.Partition, Collection, DocumentId),
                        InitialRevision, Json, false, []);
                    await Assert.That(JsonSerializer.Serialize(healthy, JsonDefaults.Options))
                        .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
                    _ = ControlledMovementUnconfiguredEngine.Create(database.Store);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
