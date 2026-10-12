using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobPublishedRecordIntegrityFaults
{
    internal static (byte[] Key, byte[] Original) Install(ZoneTreeStore store,
        BlobRangeIntegrityFixture fixture, int fault)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fault);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fault, 3);
        var key = BlobRangeIntegrityOracle.Key(fault < 2 ? BlobRangeIntegrityFixture.HeadSpace
            : BlobRangeIntegrityFixture.StateSpace, fixture);
        var original = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The actual published record is absent.");
        byte[] damaged;
        if (fault < 2)
        {
            var head = NativeSerialization.Deserialize<BlobHead>(original);
            damaged = NativeSerialization.Serialize(fault == 0
                ? head with { FormatVersion = BlobRangeIntegrityFixture.UnsupportedVersion }
                : head with { Metadata = head.Metadata with { PartCount = head.Metadata.PartCount + 1 } });
        }
        else
        {
            var state = NativeSerialization.Deserialize<BlobState>(original);
            damaged = NativeSerialization.Serialize(fault == 2
                ? state with { FormatVersion = BlobRangeIntegrityFixture.UnsupportedVersion }
                : state with { IntegrityHash = new string('0', 64) });
        }
        if (original.AsSpan().SequenceEqual(damaged))
        { throw new InvalidOperationException("The controlled record fault must change actual bytes."); }
        store.Commit((transaction, _) => { transaction.Put(key, damaged); return true; });
        return (key, original);
    }

    internal static void Repair(ZoneTreeStore store, (byte[] Key, byte[] Original) fault)
        => store.Commit((transaction, _) => { transaction.Put(fault.Key, fault.Original); return true; });
}
