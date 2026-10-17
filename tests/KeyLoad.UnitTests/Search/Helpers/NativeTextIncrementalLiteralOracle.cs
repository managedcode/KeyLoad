using KeyLoad.Core;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextIncrementalLiteralOracle
{
    private const long ChangedRevision = 2;
    private const int RecordCount = 2;
    private const ulong UkrainianRecord = 2;
    private const ulong EnglishRecord = 1;
    private static readonly string[] ChangedTerms = ["оновлено", "changed"];

    internal static async Task VerifyAsync(TestDatabase fixture,
        TextIndexMaintenanceRequest request, string changedJson, CancellationToken cancellationToken)
    {
        var database = fixture;
        var before = database.Store.Position;
        var ukrainian = database.Database.GetDocument(NativeTextMaintenanceTestValues.Principal,
            new EntityRef(database.Partition, request.Collection, NativeTextBilingualAudit.UkrainianId), cancellationToken: cancellationToken);
        var expected = new DocumentResult(new(database.Partition, request.Collection, NativeTextBilingualAudit.UkrainianId),
            ChangedRevision, changedJson, false, []);
        await Assert.That(JsonDefaults.Serialize(ukrainian).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(database.Database.GetDocument(NativeTextMaintenanceTestValues.Principal,
            new EntityRef(database.Partition, request.Collection, NativeTextBilingualAudit.EnglishId), cancellationToken: cancellationToken)).IsNull();
        var options = UnitNativeTextOptions.Execution();
        var root = Path.Combine(database.Directory, NativeTextIncrementalProtocol.RootDirectory);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            cancellationToken: cancellationToken);
        var leaf = NativeTextIncrementalEnrollmentFiles.Find(root, request, budget, options)
            ?? throw new InvalidOperationException();
        var manifest = NativeTextIncrementalMetadata.ReadManifest(Path.Combine(root, leaf),
            options.Value.MaximumDiskBytes, budget);
        await Assert.That(manifest.Records.Length).IsEqualTo(RecordCount);
        await Assert.That(manifest.Bootstrap).IsFalse();
        var active = manifest.Records.Single(record => record.Reference.Id == NativeTextBilingualAudit.UkrainianId);
        var deleted = manifest.Records.Single(record => record.Reference.Id == NativeTextBilingualAudit.EnglishId);
        await Assert.That(active.Id).IsEqualTo(UkrainianRecord);
        await Assert.That(deleted.Id).IsEqualTo(EnglishRecord);
        await Assert.That(active.Revision).IsEqualTo(ChangedRevision);
        await Assert.That(active.Deleted).IsFalse();
        await Assert.That(deleted.Revision).IsEqualTo(ChangedRevision);
        await Assert.That(deleted.Deleted).IsTrue();
        await NativeTextIncrementalPostingOracle.VerifyAsync(root, leaf, request.NodeId, UkrainianRecord, ChangedTerms, options);
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }
}
