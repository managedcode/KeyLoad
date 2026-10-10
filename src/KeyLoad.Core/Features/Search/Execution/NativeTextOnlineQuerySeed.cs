using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal static partial class NativeTextSeedCollector
{
    private const int EmptyDocumentCapacity = 0;
    private const int FirstDocumentCapacity = 1;
    private const int DocumentCapacityGrowthFactor = 2;
    internal static NativeTextSeedCapture CaptureOnlineQueryView(DatabaseEngine database, IKeyValueView raw,
        string principalId, NativeTextSeedPin pin, ReadExecutionBudget budget)
    {
        var metadata = CaptureMetadataView(database, raw, principalId, pin, budget);
        var documents = new List<DocumentRecord>();
        var scan = budget.VisitRange(raw, DocumentStorageKeys.Prefix(pin.Consumer.Partition, pin.Collection),
            database.Limits.MaxScanRecords, (key, value) =>
            {
                budget.ChargeBytes(value.Length);
                if (documents.Count == documents.Capacity)
                {
                    var capacity = documents.Capacity == EmptyDocumentCapacity ? FirstDocumentCapacity
                        : (int)Math.Min(database.Limits.MaxScanRecords, checked((long)documents.Capacity * DocumentCapacityGrowthFactor));
                    if (capacity <= documents.Count)
                    { throw Errors.Fail(ErrorCode.BudgetExceeded, SeedExceeded); }
                    budget.ChargeBytes(checked((long)capacity * IntPtr.Size));
                    documents.Capacity = capacity;
                }
                var document = NativeSerialization.Deserialize<DocumentRecord>(value);
                if (document.Reference.Partition != pin.Consumer.Partition
                    || document.Reference.Collection != pin.Collection || document.Revision <= EmptyPosition
                    || !key.SequenceEqual(DocumentStorageKeys.RecordKey(document.Reference)))
                { throw Errors.Fail(ErrorCode.Corruption, InvalidScope); }
                documents.Add(document);
                return true;
            });
        if (scan.HasMore)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SeedExceeded); }
        budget.ChargeBytes(checked((long)documents.Count * IntPtr.Size));
        return metadata with { Documents = documents.ToArray() };
    }
}
