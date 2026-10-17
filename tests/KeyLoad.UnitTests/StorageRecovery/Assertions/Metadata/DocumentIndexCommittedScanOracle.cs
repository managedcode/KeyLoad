using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class DocumentIndexCommittedScanOracle
{
    internal const string Collection = "cut-documents";
    internal const string IndexName = "by-label";
    internal const string LabelPath = "/label";
    internal const string ReplaceId = "replace";
    internal const string DeleteId = "delete";
    internal const string StableId = "stable";
    internal const string InsertId = "insert";
    internal const string HealthyId = "healthy";
    internal const string OldLabel = "old";
    internal const string DeletedLabel = "gone";
    internal const string NewLabel = "new";
    internal const string StableLabel = "keep";
    internal const string HealthyLabel = "healthy";
    internal const string OriginalJson = "{\"label\":\"old\",\"version\":1}";
    internal const string DeletedJson = "{\"label\":\"gone\",\"version\":1}";
    internal const string StableJson = "{\"label\":\"keep\",\"version\":1}";
    internal const string ReplacementJson = "{\"label\":\"new\",\"version\":2}";
    internal const string InsertedJson = "{\"label\":\"new\",\"version\":1}";
    internal const string HealthyJson = "{\"label\":\"healthy\",\"version\":1}";
    internal const int InitialRevision = 1;
    internal const int UpdatedRevision = 2;
    private const int RangeLimit = 5;
    private static readonly TimeSpan CallbackDeadline = TimeSpan.FromSeconds(10);
    private const string DeletedTombstoneJson = "{}";
    private static readonly string[] Labels = [OldLabel, DeletedLabel, NewLabel, StableLabel, HealthyLabel];

    internal static DocumentIndexCommittedScanCut ReadAndHold(IKeyValueView view, ZoneTreeStore store,
        PartitionRef partition, ManualResetEventSlim entered, ManualResetEventSlim release, CancellationToken token)
    {
        var cut = Read(view, store, partition, token);
        entered.Set();
        if (!release.Wait(CallbackDeadline, token))
        {
            throw new TimeoutException("The committed document/index read barrier expired.");
        }
        return cut;
    }

    internal static DocumentIndexCommittedScanCut ReadCut(ZoneTreeStore store, PartitionRef partition)
        => store.Read(view => Read(view, store, partition, CancellationToken.None));

    internal static async Task AssertOldCutAsync(DocumentIndexCommittedScanCut actual, long position)
    {
        await Assert.That(actual.Position).IsEqualTo(position);
        await AssertDocumentsAsync(actual.Documents,
        [
            new(DeleteId, InitialRevision, DeletedJson, false),
            new(ReplaceId, InitialRevision, OriginalJson, false),
            new(StableId, InitialRevision, StableJson, false)
        ]);
        await AssertIndexAsync(actual.IndexIds, OldLabel, [ReplaceId]);
        await AssertIndexAsync(actual.IndexIds, DeletedLabel, [DeleteId]);
        await AssertIndexAsync(actual.IndexIds, NewLabel, []);
        await AssertIndexAsync(actual.IndexIds, StableLabel, [StableId]);
        await AssertIndexAsync(actual.IndexIds, HealthyLabel, []);
    }

    internal static async Task AssertCommittedCutAsync(DocumentIndexCommittedScanCut actual, long position)
    {
        await Assert.That(actual.Position).IsEqualTo(position);
        await AssertDocumentsAsync(actual.Documents,
        [
            new(DeleteId, UpdatedRevision, DeletedTombstoneJson, true),
            new(InsertId, InitialRevision, InsertedJson, false),
            new(ReplaceId, UpdatedRevision, ReplacementJson, false),
            new(StableId, InitialRevision, StableJson, false)
        ]);
        await AssertIndexAsync(actual.IndexIds, OldLabel, []);
        await AssertIndexAsync(actual.IndexIds, DeletedLabel, []);
        await AssertIndexAsync(actual.IndexIds, NewLabel, [InsertId, ReplaceId]);
        await AssertIndexAsync(actual.IndexIds, StableLabel, [StableId]);
        await AssertIndexAsync(actual.IndexIds, HealthyLabel, []);
    }

    internal static async Task AssertHealthyCutAsync(DocumentIndexCommittedScanCut actual, long position)
    {
        await Assert.That(actual.Position).IsEqualTo(position);
        await AssertDocumentsAsync(actual.Documents,
        [
            new(DeleteId, UpdatedRevision, DeletedTombstoneJson, true),
            new(HealthyId, InitialRevision, HealthyJson, false),
            new(InsertId, InitialRevision, InsertedJson, false),
            new(ReplaceId, UpdatedRevision, ReplacementJson, false),
            new(StableId, InitialRevision, StableJson, false)
        ]);
        await AssertIndexAsync(actual.IndexIds, OldLabel, []);
        await AssertIndexAsync(actual.IndexIds, DeletedLabel, []);
        await AssertIndexAsync(actual.IndexIds, NewLabel, [InsertId, ReplaceId]);
        await AssertIndexAsync(actual.IndexIds, StableLabel, [StableId]);
        await AssertIndexAsync(actual.IndexIds, HealthyLabel, [HealthyId]);
    }

    private static DocumentIndexCommittedScanCut Read(IKeyValueView view, ZoneTreeStore store,
        PartitionRef partition, CancellationToken token)
    {
        var position = store.Position;
        var documents = new List<DocumentIndexCommittedDocument>();
        var documentRange = view.VisitRange(DocumentStorageKeys.Prefix(partition, Collection), RangeLimit, (_, value) =>
        {
            var record = NativeSerialization.Deserialize<DocumentRecord>(value);
            documents.Add(new(record.Reference.Id, record.Revision, record.Json, record.Deleted));
            return true;
        }, cancellationToken: token);
        EnsureCompleteRange(documentRange, documents.Count);

        var indexIds = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var label in Labels)
        {
            indexIds.Add(label, ReadIndexIds(view, partition, label, token));
        }
        return new(position, documents.ToArray(), indexIds);
    }

    private static string[] ReadIndexIds(IKeyValueView view, PartitionRef partition, string label, CancellationToken token)
    {
        var ids = new List<string>();
        var index = IndexName;
        var prefix = KeySpace.Partition(nameof(index), partition, Collection, index, label);
        var result = view.VisitRange(prefix, RangeLimit, (_, value) =>
        {
            ids.Add(NativeSerialization.Deserialize<string>(value));
            return true;
        }, cancellationToken: token);
        EnsureCompleteRange(result, ids.Count);
        return ids.ToArray();
    }

    private static void EnsureCompleteRange(StorageScanResult result, int copied)
    {
        if (result.HasMore || result.Records != copied)
        {
            throw new InvalidOperationException("The bounded document/index scan did not capture its complete range.");
        }
    }

    private static async Task AssertDocumentsAsync(DocumentIndexCommittedDocument[] actual,
        DocumentIndexCommittedDocument[] expected)
        => await Assert.That(actual).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);

    private static async Task AssertIndexAsync(Dictionary<string, string[]> actual, string label, string[] expected)
        => await Assert.That(actual[label]).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
}

internal sealed record DocumentIndexCommittedScanCut(long Position, DocumentIndexCommittedDocument[] Documents,
    Dictionary<string, string[]> IndexIds);

internal sealed record DocumentIndexCommittedDocument(string Id, long Revision, string Json, bool Deleted);
