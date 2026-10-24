using KeyLoad.Core.BackupRestore.Serialization;
using KeyLoad.Storage;
using static KeyLoad.RecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.EventStreams;

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
        return page.Records.ToArray();
    });

    internal static string[] Format(IEnumerable<KeyValueRecord> records, PartitionRef? partition = null)
        => records.Where(row => partition is null || Owns(row.Key.Span, partition))
            .Select(row => Convert.ToHexString(row.Key.Span) + RowSeparator + Convert.ToHexString(row.Value.Span)).ToArray();

    internal static bool Owns(ReadOnlySpan<byte> key, PartitionRef partition)
    {
        var values = KeyCodec.Decode(key);
        return (values.Length >= ScopeComponentCount && Equals(values[1], partition.TenantId)
            && Equals(values[DatabaseComponent], partition.DatabaseId) && Equals(values[DomainComponent], partition.TransactionDomainId)
            && Equals(values[PartitionKeyComponent], partition.PartitionKey))
            || key.SequenceEqual(AtomicPartitionRosterKeys.Partition(partition));
    }
}
