using KeyLoad.Core;
using KeyLoad.Core.Features.ChangeFeeds;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Owns complete canonical outbox/pin and original durable intent observations, never checkpoint authority.</summary>
internal sealed class NativeTextIncrementalExpirySnapshot
{
    private const int PreviousSequence = 1;
    private const long OriginalRevision = 1;
    private readonly string intentPath;
    private readonly byte[] intent;
    private readonly byte[] status;
    private readonly byte[] outbox;

    private NativeTextIncrementalExpirySnapshot(string intentPath, byte[] intent, byte[] status, byte[] outbox)
    {
        this.intentPath = intentPath;
        this.intent = intent;
        this.status = status;
        this.outbox = outbox;
    }

    internal static async Task<NativeTextIncrementalExpirySnapshot> CaptureAsync(TestDatabase database,
        TextIndexMaintenanceRequest request, CancellationToken cancellationToken)
    {
        var root = Path.Combine(database.Directory, NativeTextIncrementalProtocol.RootDirectory);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            cancellationToken: cancellationToken);
        var leaf = NativeTextIncrementalEnrollmentFiles.Find(root, request, budget, UnitNativeTextOptions.Execution())
            ?? throw new InvalidOperationException();
        var path = Path.Combine(root, leaf, NativeTextIncrementalProtocol.IntentFile);
        await Assert.That(new FileInfo(path).Length).IsLessThanOrEqualTo((long)database.Database.Limits.MaxBatchBytes);
        await DocumentsAsync(database, cancellationToken);
        return new(path, await File.ReadAllBytesAsync(path, cancellationToken), Status(database), Outbox(database));
    }

    internal async Task RequireAsync(TestDatabase database, CancellationToken cancellationToken)
    {
        await Assert.That(Status(database).SequenceEqual(status)).IsTrue();
        await Assert.That(Outbox(database).SequenceEqual(outbox)).IsTrue();
        await Assert.That(new FileInfo(intentPath).Length).IsEqualTo((long)intent.Length);
        await Assert.That((await File.ReadAllBytesAsync(intentPath, cancellationToken)).SequenceEqual(intent)).IsTrue();
        await DocumentsAsync(database, cancellationToken);
    }

    private static byte[] Status(TestDatabase database)
        => JsonDefaults.Serialize(database.Database.GetOutboxStatus(NativeTextMaintenanceTestValues.Principal, database.Partition));

    private static byte[] Outbox(TestDatabase database)
        => database.Store.Read(view =>
        {
            var head = database.Database.ReadOutboxHead(view, database.Partition);
            var records = StoredOutboxReader.ReadRange(view, database.Partition,
                checked(head.FirstAvailable - PreviousSequence), head.Tail, checked((int)head.StoredRecords)).ToArray();
            return JsonDefaults.Serialize(records.Select(record => view.ReadOwnedValue(record.Key)
                ?? throw new InvalidOperationException()).ToArray());
        });

    private static async Task DocumentsAsync(TestDatabase database, CancellationToken cancellationToken)
    {
        await DocumentAsync(database, NativeTextBilingualAudit.UkrainianId, NativeTextBilingualAudit.UkrainianJson, cancellationToken);
        await DocumentAsync(database, NativeTextBilingualAudit.EnglishId, NativeTextBilingualAudit.EnglishJson, cancellationToken);
    }

    private static async Task DocumentAsync(TestDatabase database, string id, string json, CancellationToken cancellationToken)
    {
        var reference = new EntityRef(database.Partition, NativeTextBilingualAudit.Collection, id);
        var actual = database.Database.GetDocument(NativeTextMaintenanceTestValues.Principal, reference,
            cancellationToken: cancellationToken);
        var expected = new DocumentResult(reference, OriginalRevision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
