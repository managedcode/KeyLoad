using KeyLoad.Query;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class BackupPersistedPolicyAssertions
{
    private const string Predicate = "SELECT * FROM backup_policy_documents WHERE secret = 'CANARY'";
    private const int MaximumFixtureRows = 4096;

    internal static (string[] Rows, long Position) Capture(IAtomicStore store)
        => store.Read(view =>
        {
            var page = view.Scan([], MaximumFixtureRows);
            if (page.HasMore)
            {
                throw new InvalidOperationException("Whole store state exceeded the fixture bound.");
            }
            return (page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
                Convert.ToHexString(record.Value.Span)).ToArray(), store.Position);
        });

    internal static async Task UnchangedAsync(IAtomicStore store, (string[] Rows, long Position) cut)
    {
        var current = Capture(store);
        await Assert.That(current.Rows).IsEquivalentTo(cut.Rows, CollectionOrdering.Matching);
        await Assert.That(current.Position).IsEqualTo(cut.Position);
    }

    internal static async Task CurrentAsync(BackupIndexedDocumentDedupFixture fixture,
        DatabaseEngine database, bool repaired, CancellationToken token)
    {
        var cut = Capture(database.Store);
        var principal = database.Store.Read(view => StorageRecords.GetRecord<PrincipalRecord>(view,
            KeySpace.Principal(BackupPersistedPolicySeed.Reader)));
        var live = database.Store.Read(view => StorageRecords.GetRecord<ApiKeyRecord>(view,
            KeySpace.ApiKey(BackupPersistedPolicySeed.LiveKeyId)));
        var revoked = database.Store.Read(view => StorageRecords.GetRecord<ApiKeyRecord>(view,
            KeySpace.ApiKey(BackupPersistedPolicySeed.RevokedKeyId)));
        var resource = database.Store.Read(view => StorageRecords.GetRecord<ResourceDefinition>(view,
            KeySpace.Resource(fixture.Partition.TenantId, fixture.Partition.DatabaseId, BackupPersistedPolicySeed.Collection)));
        await SameAsync(BackupPersistedPolicySeed.Resource(fixture.Partition), resource);
        await SameAsync(BackupPersistedPolicySeed.Principal(fixture.Partition, repaired), principal);
        await SameAsync(BackupPersistedPolicySeed.LiveKey, live);
        await SameAsync(BackupPersistedPolicySeed.RevokedKey, revoked);
        await Assert.That(database.Authenticate(BackupPersistedPolicySeed.LiveSecret,
            database.EvaluationClock.GetUtcNow())).IsEqualTo(BackupPersistedPolicySeed.Reader);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => database.Authenticate(
            BackupPersistedPolicySeed.RevokedSecret, database.EvaluationClock.GetUtcNow()));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.Unauthenticated);
        var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(fixture.Partition, Predicate, AllowFullScan: true);
        if (repaired)
        {
            var page = queries.Execute(BackupPersistedPolicySeed.Reader, request, cancellationToken: token);
            QueryRow[] rows = [new(BackupPersistedPolicySeed.DocumentId, 1L, BackupPersistedPolicySeed.Json, false, [])];
            await SameAsync(rows, page.Rows.ToArray());
            await Assert.That(page.Cursor).IsNull();
            await Assert.That(page.CutPosition).IsEqualTo(cut.Position);
            var reference = fixture.Source.ExpectedReference(new(fixture.Partition,
                BackupPersistedPolicySeed.Collection, BackupPersistedPolicySeed.DocumentId));
            await SameAsync(new DocumentResult(reference, 1L, BackupPersistedPolicySeed.Json, false, []),
                database.GetDocument(BackupPersistedPolicySeed.Reader, reference, cancellationToken: token));
        }
        else
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute(
                BackupPersistedPolicySeed.Reader, request, cancellationToken: token));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        }
        await UnchangedAsync(database.Store, cut);
    }

    internal static async Task ReplayAsync(DatabaseEngine database, Command<DatabaseCommand> operation,
        Result<object> original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var cut = Capture(database.Store);
        await SameAsync(original, database.Apply(operation));
        await UnchangedAsync(database.Store, cut);
    }

    internal static async Task ReceiptAsync(DatabaseEngine database, PartitionRef partition, Guid id, CommitReceipt receipt)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.Position).IsEqualTo(database.LastApplied);
        var owner = database.Store.Read(view => database.ReadPlacementWitness(view, partition));
        await SameAsync(new CommitToken(owner.Incarnation, partition.AtomicPartitionId,
            database.LastApplied), receipt.Token);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(database.Store.Identity.Durability);
    }

    internal static async Task SameAsync<T>(T expected, T actual)
        => await Assert.That(OrleansSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
}
