using System.Text.Json;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentMutationImageReadTests
{
    private const string Collection = "orders";
    private const string DocumentId = "image";
    private const string SmallJson = "{\"value\":\"a\"}";
    private const string UpdatedJson = "{\"value\":\"updated\"}";
    private const int LargeValueLength = 65_536;
    private const long MetadataByteTolerance = 2_048;
    private const long MutationAndOutcomeBorrowedPoints = 6;
    private const long PlacementWitnessBorrowedPoints = 3;
    private const long ExistingRosterOriginBorrowedPoints = 1;
    private const long NewPutBorrowedPoints = MutationAndOutcomeBorrowedPoints
        + PlacementWitnessBorrowedPoints;
    private const long ExistingMutationBorrowedPoints = NewPutBorrowedPoints
        + ExistingRosterOriginBorrowedPoints;

    [Test]
    public async Task AcDstore005NewPutHasOneDocumentMissAndNoPayloadSizedPointRead()
    {
        var small = await MeasurePut(SmallJson);
        var large = await MeasurePut(LargeJson());

        await Assert.That(small.Points).IsEqualTo(NewPutBorrowedPoints);
        await Assert.That(large.Points).IsEqualTo(NewPutBorrowedPoints);
        await Assert.That(large.Bytes - small.Bytes).IsLessThanOrEqualTo(MetadataByteTolerance);
    }

    [Test]
    public async Task AcDstore005ReplacementPatchAndDeleteReadOnePriorImage()
    {
        foreach (var action in Enum.GetValues<ImageAction>())
        {
            var small = await MeasureExisting(SmallJson, action);
            var large = await MeasureExisting(LargeJson(), action);
            var payloadDelta = LargeValueLength - 1;

            await Assert.That(small.Points).IsEqualTo(ExistingMutationBorrowedPoints);
            await Assert.That(large.Points).IsEqualTo(ExistingMutationBorrowedPoints);
            await Assert.That(Math.Abs((large.Bytes - small.Bytes) - payloadDelta))
                .IsLessThanOrEqualTo(MetadataByteTolerance);
        }
    }

    private static async Task<(long Points, long Bytes)> MeasurePut(string json)
    {
        using var db = PreparedDatabase();
        return await MeasureMutation(db, new PutDocument(Collection, DocumentId, json),
            "putDocument", 1, json);
    }

    private static async Task<(long Points, long Bytes)> MeasureExisting(string json, ImageAction action)
    {
        using var db = PreparedDatabase();
        await MeasureMutation(db, new PutDocument(Collection, DocumentId, json), "putDocument", 1, json);
        Mutation mutation = action switch
        {
            ImageAction.Replace => new PutDocument(Collection, DocumentId, UpdatedJson, 1, ExplicitReplacement: true),
            ImageAction.Patch => new PatchDocument(Collection, DocumentId, [new("/value", PatchKind.Set, "\"updated\"")], 1),
            ImageAction.Delete => new DeleteDocument(Collection, DocumentId, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        var kind = action switch
        {
            ImageAction.Replace => "putDocument",
            ImageAction.Patch => "patchDocument",
            ImageAction.Delete => "deleteDocument",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        return await MeasureMutation(db, mutation, kind, 2, action == ImageAction.Delete ? null : UpdatedJson);
    }

    private static async Task<(long Points, long Bytes)> MeasureMutation(
        TestDatabase db, Mutation mutation, string kind, long revision, string? json)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, db.Partition, [mutation]);
        var before = db.Store.GetReadDiagnostics();
        var receipt = db.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        var delta = Delta(db, before);
        var expected = new CommitReceipt(id, new(db.Store.Identity.Incarnation, db.Partition.AtomicPartitionId,
            db.Store.Position, 1), [new(kind, Collection, DocumentId, revision)], db.Store.Identity.Durability);

        await Assert.That(NativeSerialization.Serialize(receipt).SequenceEqual(
            NativeSerialization.Serialize(expected))).IsTrue();
        await RequireDocumentImage(db, revision, json);
        var replay = db.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(
            NativeSerialization.Serialize(expected))).IsTrue();
        await RequireDocumentImage(db, revision, json);
        return delta;
    }

    private static async Task RequireDocumentImage(TestDatabase db, long revision, string? json)
    {
        var reference = new EntityRef(db.Partition, Collection, DocumentId);
        DocumentResult? expected = json is null ? null : new(reference, revision, json, false, []);
        var actual = db.Database.GetDocument("root", reference);
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(revision);
    }

    private static TestDatabase PreparedDatabase()
    {
        var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        return db;
    }

    private static (long Points, long Bytes) Delta(TestDatabase db, KeyLoad.Storage.ZoneTree.ZoneTreeReadSnapshot before)
    {
        var after = db.Store.GetReadDiagnostics();
        return (after.BorrowedPointLookups - before.BorrowedPointLookups,
            after.PointExaminedBytes - before.PointExaminedBytes);
    }

    private static string LargeJson() => "{\"value\":\"" + new string('x', LargeValueLength) + "\"}";

    private enum ImageAction { Replace, Patch, Delete }
}
