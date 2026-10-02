using KeyLoad.Core;

using static KeyLoad.UnitTests.Features.ChangeFeeds.ChangeFeedTestActions;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed class ProjectionReadWorkTests
{
    private const string Resource = "orders";
    private const string PrincipalId = "root";
    private const string ConsumerName = "read-work";
    private const string DocumentId = "unicode";
    private const string OutboxSpace = "outbox";
    private const string JsonPrefix = "{\"content\":\"";
    private const string JsonSuffix = "\"}";
    private const char ContentRune = 'λ';
    private const int ContentRuneCount = 8_192;

    [Test]
    public async Task AcMp006ProjectionBatchReusesHeadAndStoredEntryBytes()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.Collection);
        var document = string.Concat(JsonPrefix, new string(ContentRune, ContentRuneCount), JsonSuffix);
        database.Commit(new PutDocument(Resource, DocumentId, document));
        var consumer = Consumer(database, ConsumerName);
        Configure(database, consumer);

        var key = KeySpace.Partition(OutboxSpace, database.Partition, 1);
        var stored = database.Store.Read(view => view.ReadOwnedValue(key))!;
        var entry = JsonDefaults.Deserialize<OutboxEntry>(stored)!;
        var serialized = JsonDefaults.Serialize(entry);
        var storedBytesMatchCanonical = stored.AsSpan().SequenceEqual(serialized);
        await Assert.That(storedBytesMatchCanonical).IsTrue();

        var before = database.Store.GetReadDiagnostics();
        var batch = database.Database.ReadProjectionBatch(PrincipalId,
            new(consumer, Limit: 1, MaxBytes: stored.Length));
        var after = database.Store.GetReadDiagnostics();

        await Assert.That(batch.Entries).HasSingleItem();
        await Assert.That(batch.ThroughSequence).IsEqualTo(1L);
        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo(4L);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            database.Database.ReadProjectionBatch(PrincipalId,
                new(consumer, Limit: 1, MaxBytes: stored.Length - 1))).Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }
}
