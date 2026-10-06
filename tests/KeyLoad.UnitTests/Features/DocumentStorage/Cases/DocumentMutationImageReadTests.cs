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
    private const long AtomicRosterValidationBorrowedPoints = 1;
    private const long SingleMutationBorrowedPoints = MutationAndOutcomeBorrowedPoints
        + PlacementWitnessBorrowedPoints + AtomicRosterValidationBorrowedPoints;

    [Test]
    public async Task AcDstore005NewPutHasOneDocumentMissAndNoPayloadSizedPointRead()
    {
        var small = MeasurePut(SmallJson);
        var large = MeasurePut(LargeJson());

        await Assert.That(small.Points).IsEqualTo(SingleMutationBorrowedPoints);
        await Assert.That(large.Points).IsEqualTo(SingleMutationBorrowedPoints);
        await Assert.That(large.Bytes - small.Bytes).IsLessThanOrEqualTo(MetadataByteTolerance);
    }

    [Test]
    public async Task AcDstore005ReplacementPatchAndDeleteReadOnePriorImage()
    {
        foreach (var action in Enum.GetValues<ImageAction>())
        {
            var small = MeasureExisting(SmallJson, action);
            var large = MeasureExisting(LargeJson(), action);
            var payloadDelta = LargeValueLength - 1;

            await Assert.That(small.Points).IsEqualTo(SingleMutationBorrowedPoints);
            await Assert.That(large.Points).IsEqualTo(SingleMutationBorrowedPoints);
            await Assert.That(Math.Abs((large.Bytes - small.Bytes) - payloadDelta))
                .IsLessThanOrEqualTo(MetadataByteTolerance);
        }
    }

    private static (long Points, long Bytes) MeasurePut(string json)
    {
        using var db = PreparedDatabase();
        var before = db.Store.GetReadDiagnostics();
        db.Commit(new PutDocument(Collection, DocumentId, json));
        return Delta(db, before);
    }

    private static (long Points, long Bytes) MeasureExisting(string json, ImageAction action)
    {
        using var db = PreparedDatabase();
        db.Commit(new PutDocument(Collection, DocumentId, json));
        var before = db.Store.GetReadDiagnostics();
        db.Commit(action switch
        {
            ImageAction.Replace => new PutDocument(Collection, DocumentId, UpdatedJson, 1, ExplicitReplacement: true),
            ImageAction.Patch => new PatchDocument(Collection, DocumentId, [new("/value", PatchKind.Set, "\"updated\"")], 1),
            ImageAction.Delete => new DeleteDocument(Collection, DocumentId, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        });
        return Delta(db, before);
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
