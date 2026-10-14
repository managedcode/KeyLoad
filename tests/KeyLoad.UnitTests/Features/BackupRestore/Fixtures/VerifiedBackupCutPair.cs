using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class VerifiedBackupCutPair
{
    private const string Earlier = "verified-earlier-cut";
    private const string Later = "verified-later-cut";
    private const string DocumentId = "second-original-cut";
    private const string DocumentJson = "{\"cut\":\"second\",\"verified\":true}";
    private const long MissingVersion = 0;

    internal static async Task<(VerifiedBackupCutSnapshot Earlier, VerifiedBackupCutSnapshot Later)> CaptureAsync(
        EventingArtifactFixture fixture, CancellationToken token)
    {
        var earlier = await CaptureAsync(fixture, Earlier, token);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, fixture.Source.Partition,
            [new PutDocument(EventingArtifactFixture.Documents, DocumentId, DocumentJson, MissingVersion)]);
        var issued = fixture.Operation(OperationKind.Batch, command, id);
        fixture.SubmitIssued(fixture.Source.Database, issued, explicitTime: false, cancellationToken: token)
            .Get<CommitReceipt>();
        var later = await CaptureAsync(fixture, Later, token);
        await Assert.That(later.Capture.Position > earlier.Capture.Position).IsTrue();
        await Assert.That(later.PartitionRows.Length > earlier.PartitionRows.Length).IsTrue();
        return (earlier, later);
    }

    private static async Task<VerifiedBackupCutSnapshot> CaptureAsync(EventingArtifactFixture fixture,
        string name, CancellationToken token)
    {
        var path = System.IO.Path.Combine(fixture.Root, name);
        var capture = MixedEventingCatalogRestore.Capture(fixture.Source, path, token);
        var verification = ZoneTreeBackupVerification.Verify(path, fixture.Source.StorageExecution);
        await Assert.That(verification.Position).IsEqualTo(capture.Position);
        await Assert.That(verification.Identity.NodeId).IsEqualTo(fixture.Source.Store.Identity.NodeId);
        await Assert.That(verification.Identity.Incarnation).IsEqualTo(fixture.Source.Store.Identity.Incarnation);
        await Assert.That(verification.Identity.FormatVersion).IsEqualTo(fixture.Source.Store.Identity.FormatVersion);
        await Assert.That(verification.Identity.SigningKey.Span.SequenceEqual(fixture.Source.Store.Identity.SigningKey.Span)).IsTrue();
        var rows = EventingArtifactState.Bytes(fixture.Source.Store, fixture.Source.Partition);
        var archive = await CatalogRestoreCancellationFlow.ArchiveAsync(path, token);
        return new(path, capture, rows, archive);
    }
}
