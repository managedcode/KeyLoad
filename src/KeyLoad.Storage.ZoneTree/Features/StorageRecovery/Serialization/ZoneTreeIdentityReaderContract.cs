using System.Buffers.Binary;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeIdentityReaderContract
{
    private const ulong RuntimeJournalIdentityMagic = 0x364449444C4BUL;

    internal static ulong ReadMagic(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length <= sizeof(ulong))
        {
            throw Unsupported();
        }
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        if (magic is not (ZoneTreeMetadataBinary.IdentityMagic or RuntimeJournalIdentityMagic))
        {
            throw Unsupported();
        }
        return magic;
    }

    internal static ulong WriteMagic(StoreIdentity identity) => identity.MinimumReaderContract switch
    {
        StoreReaderContract.Legacy => ZoneTreeMetadataBinary.IdentityMagic,
        StoreReaderContract.RuntimeJournal => RuntimeJournalIdentityMagic,
        _ => throw Unsupported()
    };

    internal static void Validate(StoreIdentity identity, ulong magic)
    {
        if (WriteMagic(identity) != magic)
        {
            throw Unsupported();
        }
    }

    private static KeyLoadException Unsupported()
        => Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.IdentityFormatUnsupported);
}
