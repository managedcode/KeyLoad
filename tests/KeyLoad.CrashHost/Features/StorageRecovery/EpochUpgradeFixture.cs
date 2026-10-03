using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class EpochUpgradeFixture
{
    internal const string Namespace = "epoch-upgrade";
    internal const long AppliedPosition = 73;
    private const string MarkerKeyName = "marker";
    private const string RecordKeyName = "record";
    private const string SystemNamespace = "system";
    private const string LastAppliedKey = "last-applied";

    internal static byte[] FirstKey => KeyCodec.Encode(Namespace, RecordKeyName, 0L);
    internal static byte[] SecondKey => KeyCodec.Encode(Namespace, RecordKeyName, 1L);
    internal static byte[] MarkerKey => KeyCodec.Encode(Namespace, MarkerKeyName);
    internal static byte[] AppliedKey => KeyCodec.Encode(SystemNamespace, LastAppliedKey);
    internal static byte[] FirstValue => [0x00, 0x7F, 0x80, 0xFF];
    internal static byte[] SecondValue => [0x11, 0x00, 0xFE, 0x22];
    internal static byte[] MarkerValue => [0xE0, 0x00, 0x0D];

    internal static void Seed(ZoneTreeStore store, bool compacted)
    {
        ArgumentNullException.ThrowIfNull(store);
        store.SetDispatchPaused(true);
        store.Commit((transaction, _) =>
        {
            transaction.Put(FirstKey, FirstValue);
            transaction.Put(SecondKey, SecondValue);
            transaction.Put(MarkerKey, MarkerValue);
            transaction.PutRecord(AppliedKey, AppliedPosition);
            return true;
        });
        if (compacted)
        {
            store.Compact();
        }
    }

    internal static (byte[] Key, byte[] Value)[] RawRecords() =>
    [
        (FirstKey, FirstValue),
        (SecondKey, SecondValue),
        (MarkerKey, MarkerValue)
    ];
}
