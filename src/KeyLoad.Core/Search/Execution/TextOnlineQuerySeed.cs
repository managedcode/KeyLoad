using KeyLoad.Core.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core.Search;

internal static partial class TextSeedCollector
{
    private const int DocumentCapacityGrowthFactor = 2;
    private const string SeedExceeded = "The text projection seed exceeds the largest array the runtime can allocate.";
    internal static TextSeedCapture CaptureOnlineQueryView(DatabaseEngine database, IKeyValueView raw,
        string principalId, TextSeedPin pin, ReadExecutionBudget budget)
    {
        var metadata = CaptureMetadataView(database, raw, principalId, pin, budget);
        var documents = new List<DocumentRecord>();
        _ = budget.VisitAll(raw, DocumentStorageKeys.Prefix(pin.Consumer.Partition, pin.Collection), (key, value) =>
            {
                if (documents.Count == documents.Capacity)
                {
                    var capacity = documents.Capacity == 0 ? 1
                        : (int)Math.Min(Array.MaxLength, checked((long)documents.Capacity * DocumentCapacityGrowthFactor));
                    if (capacity <= documents.Count)
                    {
                        throw Errors.Fail(ErrorCode.BudgetExceeded, SeedExceeded);
                    }
                    documents.Capacity = capacity;
                }
                var document = OrleansSerialization.Deserialize<DocumentRecord>(value);
                if (document.Reference.Partition != pin.Consumer.Partition
                    || document.Reference.Collection != pin.Collection || document.Revision <= EmptyPosition
                    || !key.SequenceEqual(DocumentStorageKeys.RecordKey(document.Reference)))
                {
                    throw Errors.Fail(ErrorCode.Corruption, InvalidScope);
                }
                documents.Add(document);
                return true;
            });
        return metadata with
        {
            Documents = documents.ToArray()
        };
    }
}
