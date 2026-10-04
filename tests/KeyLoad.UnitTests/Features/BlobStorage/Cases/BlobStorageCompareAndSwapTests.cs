using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobStorageCompareAndSwapTests
{
    private const string PrincipalId = "root";
    private const string BlobId = "cas-object";
    private const string FirstContent = "first";
    private const string CompetingContent = "competing";
    private const string WinningOverwrite = "second revision";
    private const long InitialRevision = 0;
    private const long FirstRevision = 1;

    [Test]
    public async Task AcBlob001CompetingExpectedRevisionUploadsHaveOneWinnerAndHealthyOverwrite()
    {
        using var db = new TestDatabase();
        BlobStorageTestSupport.Configure(db);
        var operations = new BlobStorageOperations(db.Database);
        var blob = BlobStorageTestSupport.Blob(db, BlobId);
        var firstUpload = StartAndWrite(db, blob, FirstContent, InitialRevision);
        var competingUpload = StartAndWrite(db, blob, CompetingContent, InitialRevision);
        var firstHash = Chain(db, blob, firstUpload, FirstContent);

        var winner = BlobStorageTestSupport.Complete(db, blob, firstUpload, firstHash).Value;
        var rejected = BlobStorageTestSupport.CompleteResult(db, blob, competingUpload,
            Chain(db, blob, competingUpload, CompetingContent));

        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(operations.Metadata(PrincipalId, new(blob))!.Revision).IsEqualTo(FirstRevision);
        await Assert.That(operations.Read(PrincipalId, new(blob, winner.Revision, 0, FirstContent.Length))
            .Bytes.Span.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(FirstContent))).IsTrue();

        var overwrite = StartAndWrite(db, blob, WinningOverwrite, FirstRevision);
        var newMetadata = BlobStorageTestSupport.Complete(db, blob, overwrite,
            Chain(db, blob, overwrite, WinningOverwrite)).Value;
        var latest = operations.Read(PrincipalId, new(blob, newMetadata.Revision, 0, WinningOverwrite.Length));
        await Assert.That(newMetadata.Revision).IsEqualTo(FirstRevision + 1);
        await Assert.That(latest.Bytes.Span.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(WinningOverwrite))).IsTrue();
    }

    private static Guid StartAndWrite(TestDatabase db, BlobRef blob,
        string content, long expectedRevision)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var uploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(db, blob, uploadId, bytes.Length, expectedRevision);
        BlobStorageTestSupport.Write(db, blob, uploadId, 0, bytes);
        return uploadId;
    }

    private static string Chain(TestDatabase db, BlobRef blob, Guid uploadId, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var chain = BlobIntegrity.InitialHash(db.Store.Identity.Incarnation, blob, uploadId, bytes.Length);
        return BlobIntegrity.NextHash(chain, 0, bytes.Length, BlobIntegrity.PartHash(bytes));
    }
}
