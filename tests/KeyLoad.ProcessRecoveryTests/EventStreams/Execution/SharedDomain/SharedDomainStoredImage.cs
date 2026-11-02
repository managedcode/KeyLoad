using KeyLoad.Core.BackupRestore.Serialization;
using KeyLoad.Storage;
using static KeyLoad.ProcessRecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.ProcessRecoveryTests.EventStreams;

internal static class SharedDomainStoredImage
{
    private const int DatabaseComponent = 2;
    private const int DomainComponent = 3;
    private const int PartitionKeyComponent = 4;
    private const int ScopeComponentCount = 5;
    private const string RowSeparator = ":";

    internal static string[] Capture(IAtomicStore store, PartitionRef? partition = null)
        => Format(CaptureRecords(store), partition);

    internal static KeyValueRecord[] CaptureRecords(IAtomicStore store) => store.Read(view =>
    {
        var page = view.Scan([], ImageBound);
        if (page.HasMore)
        {
            throw new InvalidOperationException(ImageLimit);
        }
        return page.Records.Where(stored => !StorePositionRecord.Is(stored.Key.Span)).ToArray();
    });

    internal static string[] Format(IEnumerable<KeyValueRecord> records, PartitionRef? partition = null)
        => records.Where(row => partition is null || Owns(row.Key.Span, partition))
            .Select(row => Convert.ToHexString(row.Key.Span) + RowSeparator + Convert.ToHexString(row.Value.Span)).ToArray();

    internal static bool Owns(ReadOnlySpan<byte> key, PartitionRef partition)
    {
        var values = KeyCodec.Decode(key);
        return (values.Length >= ScopeComponentCount && values[1] == KeyLoad.Storage.KeyComponent.FromString(partition.TenantId)
            && values[DatabaseComponent] == KeyLoad.Storage.KeyComponent.FromString(partition.DatabaseId) && values[DomainComponent] == KeyLoad.Storage.KeyComponent.FromString(partition.TransactionDomainId)
            && values[PartitionKeyComponent] == KeyLoad.Storage.KeyComponent.FromString(partition.PartitionKey))
            || key.SequenceEqual(AtomicPartitionRosterKeys.Partition(partition));
    }
}
