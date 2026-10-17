using System.Text.Json;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CatalogArchiveRetryAssertions
{
    internal const string LaterId = "after-original-capture";
    private const string LaterJson = "{\"afterCapture\":true}";

    internal static async Task<(ReplicatedOperation Operation, OperationResult Result, byte[] Outcome)> AdvanceAsync(EventingArtifactFixture fixture,
        CancellationToken token)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, fixture.Source.Partition,
            [new PutDocument(EventingArtifactFixture.Documents, LaterId, LaterJson, 0)]);
        var operation = fixture.Operation(OperationKind.Batch, command, id);
        var result = fixture.SubmitIssued(fixture.Source.Database, operation, explicitTime: false,
            cancellationToken: token);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Get<CommitReceipt>().CommandId).IsEqualTo(id);
        await DocumentAsync(fixture);
        var outcome = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
            fixture.Source.Partition, EventingArtifactFixture.Principal, id);
        return (operation, result, outcome);
    }

    private static async Task DocumentAsync(EventingArtifactFixture fixture)
    {
        var reference = new EntityRef(fixture.Source.Partition, EventingArtifactFixture.Documents, LaterId);
        var document = fixture.Source.Database.GetDocument(EventingArtifactFixture.Principal, reference);
        await Assert.That(document).IsNotNull();
        if (document is null)
        {
            throw new InvalidOperationException("The admitted later document was not retained.");
        }
        await Assert.That(document.Reference).IsEqualTo(reference);
        await Assert.That(document.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo(LaterJson);
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
    }

    internal static async Task CaptureAsync(NativeCatalogBackupCapture original, NativeCatalogBackupCapture actual)
    {
        await Assert.That(actual.Position).IsEqualTo(original.Position);
        await Assert.That(actual.Metadata.Span.SequenceEqual(original.Metadata.Span)).IsTrue();
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(original, JsonDefaults.Options));
    }

    internal static async Task<string[]> FilesAsync(string backup, CancellationToken token)
    {
        var result = new List<string>();
        foreach (var file in Directory.GetFiles(backup, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            result.Add(Path.GetRelativePath(backup, file) + ":" +
                Convert.ToHexString(await File.ReadAllBytesAsync(file, token).ConfigureAwait(false)));
        }
        return result.ToArray();
    }

    internal static async Task RefuseAndRepairAsync(EventingArtifactFixture fixture, string backup,
        NativeCatalogBackupCapture original, CancellationToken token)
    {
        var path = Path.Combine(backup, MetadataTestContract.ManifestFileName);
        var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        var manifest = await MetadataTestFiles.ReadManifestAsync(backup).ConfigureAwait(false);
        await MetadataTestFiles.WriteManifestAsync(backup,
            manifest with
            {
                Version = MetadataTestContract.UnsupportedManifestVersion
            }).ConfigureAwait(false);
        var archive = await FilesAsync(backup, token);
        var source = EventingArtifactState.FullBytes(fixture.Source.Store);
        var position = fixture.Source.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => CatalogArchiveRetryFlow.Capture(fixture, backup, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(failure.Message).IsEqualTo(MetadataTestContract.ManifestUnsupportedDetail);
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(fixture.Source.Store))
            .IsEquivalentTo(source, CollectionOrdering.Matching);
        await Assert.That(await FilesAsync(backup, token)).IsEquivalentTo(archive, CollectionOrdering.Matching);
        await File.WriteAllBytesAsync(path, bytes, token).ConfigureAwait(false);
        await CaptureAsync(original, CatalogArchiveRetryFlow.Capture(fixture, backup, token));
    }

    internal static async Task ReplayAsync(EventingArtifactFixture fixture,
        (ReplicatedOperation Operation, OperationResult Result, byte[] Outcome) original, string[] image, long position)
    {
        await EventingArtifactState.SameResultAsync(original.Result,
            fixture.SubmitIssued(fixture.Source.Database, original.Operation, explicitTime: false));
        await DocumentAsync(fixture);
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(fixture.Source.Store))
            .IsEquivalentTo(image, CollectionOrdering.Matching);
        await Assert.That(BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
            fixture.Source.Partition, EventingArtifactFixture.Principal, original.Operation.Id))
            .IsEquivalentTo(original.Outcome, CollectionOrdering.Matching);
    }
}
