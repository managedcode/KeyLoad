using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeFormatUpgradeSource : IDisposable
{
    private const int FileStartPosition = 0;
    private const int NoFileAttributes = 0;

    private readonly FileStream ownership;

    private ZoneTreeFormatUpgradeSource(string directory, StoreIdentity identity, string identityDigest,
        string journalDigest, long position, FileStream ownership)
    {
        Directory = directory;
        Identity = identity;
        IdentityDigest = identityDigest;
        JournalDigest = journalDigest;
        Position = position;
        this.ownership = ownership;
    }

    internal string Directory { get; }
    internal StoreIdentity Identity { get; }
    internal string IdentityDigest { get; }
    internal string JournalDigest { get; }
    internal long Position { get; }

    public void Dispose() => ownership.Dispose();

    internal void VerifyUnchanged()
    {
        ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(Directory, allowMissingFinal: false);
        var identityPath = Path.Combine(Directory, ZoneTreePersistenceFormat.IdentityFileName);
        var journalPath = Path.Combine(Directory, ZoneTreePersistenceFormat.JournalFileName);
        VerifyRegularFile(identityPath);
        VerifyRegularFile(journalPath);
        var identity = ZoneTreeMetadataFile.Read(identityPath, ZoneTreePersistenceFormat.MaximumIdentityFileBytes,
            ZoneTreePersistenceFormat.IdentityFormatUnsupported);
        using var journal = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            ZoneTreePersistenceFormat.FileBufferBytes, FileOptions.SequentialScan);
        if (!string.Equals(Digest(identity.Span), IdentityDigest, StringComparison.Ordinal)
            || !string.Equals(Digest(journal), JournalDigest, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.BackupFileVerificationFailed);
        }
    }

    internal static ZoneTreeFormatUpgradeSource Open(string directory, ZoneTreeStoreOptions options)
    {
        VerifyDirectory(directory);
        var ownerPath = Path.Combine(directory, ZoneTreePersistenceFormat.OwnerLockFileName);
        VerifyRegularFile(ownerPath);
        FileStream? ownership = null;
        try
        {
            ownership = new FileStream(ownerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var source = ReadLocked(directory, options, ownership);
            ownership = null;
            return source;
        }
        finally
        {
            ownership?.Dispose();
        }
    }

    private static ZoneTreeFormatUpgradeSource ReadLocked(string directory, ZoneTreeStoreOptions options,
        FileStream ownership)
    {
        var identityPath = Path.Combine(directory, ZoneTreePersistenceFormat.IdentityFileName);
        var journalPath = Path.Combine(directory, ZoneTreePersistenceFormat.JournalFileName);
        VerifyRegularFile(identityPath);
        VerifyRegularFile(journalPath);
        var identityBytes = ZoneTreeMetadataFile.Read(identityPath, ZoneTreePersistenceFormat.MaximumIdentityFileBytes,
            ZoneTreePersistenceFormat.IdentityFormatUnsupported);
        var identity = ZoneTreeIdentityFile.ReadStoppedSourceForUpgrade(identityBytes.Span);
        VerifyAuthority(identity, options);
        using var journal = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            ZoneTreePersistenceFormat.FileBufferBytes, FileOptions.SequentialScan);
        var position = ZoneTreeFormatUpgradeJournal.ValidateSource(journal, options, identity.Incarnation,
            identity.FormatVersion);
        journal.Position = FileStartPosition;
        var journalDigest = Digest(journal);
        return new(directory, identity, Digest(identityBytes.Span), journalDigest, position, ownership);
    }

    private static void VerifyAuthority(StoreIdentity identity, ZoneTreeStoreOptions options)
    {
        if (options.Incarnation is { } incarnation && incarnation != identity.Incarnation
            || options.SigningKey is { } signingKey
                && !CryptographicOperations.FixedTimeEquals(signingKey.Span, identity.SigningKey.Span))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ZoneTreePersistenceFormat.IdentityScopeInvalid);
        }
    }

    private static string Digest(FileStream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void VerifyDirectory(string directory)
    {
        ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(directory, allowMissingFinal: false);
        var info = new DirectoryInfo(directory);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != NoFileAttributes)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.IdentityFormatUnsupported);
        }
    }

    private static void VerifyRegularFile(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != NoFileAttributes)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.IdentityFormatUnsupported);
        }
    }
}

internal static class ZoneTreeFormatUpgradeJournal
{
    private const int FileStartPosition = 0;
    private const int InitialAppliedPosition = 0;
    private const int EmptyFrameLength = 0;
    private const int NextJournalSequenceIncrement = 1;

    internal static long ValidateCurrent(FileStream journal, ZoneTreeStoreOptions options, Guid expectedIncarnation)
    {
        journal.Position = FileStartPosition;
        var position = ZoneTreeFormatUpgradeCheckpoint.ReadCurrent(journal, options, expectedIncarnation);
        var header = new byte[ZoneTreePersistenceFormat.HeaderLength];
        while (journal.Position < journal.Length)
        {
            if (journal.Length - journal.Position < header.Length)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid);
            }
            journal.ReadExactly(header);
            position = ValidateAndReadFrame(journal, options, header, position);
        }
        return position;
    }

    internal static long ValidateSource(FileStream journal, ZoneTreeStoreOptions options, Guid expectedIncarnation,
        int sourceDataEpoch)
    {
        journal.Position = FileStartPosition;
        var position = ZoneTreeFormatUpgradeCheckpoint.ReadSource(journal, options, expectedIncarnation,
            sourceDataEpoch);
        var header = new byte[ZoneTreePersistenceFormat.HeaderLength];
        while (journal.Position < journal.Length)
        {
            if (journal.Length - journal.Position < header.Length)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid);
            }
            journal.ReadExactly(header);
            position = ValidateAndReadFrame(journal, options, header, position);
        }
        return position;
    }

    internal static long ReplaySource(FileStream journal, ZoneTreeStoreOptions options, ZoneTreeStoreRuntime runtime,
        int sourceDataEpoch)
    {
        journal.Position = FileStartPosition;
        var position = ZoneTreeFormatUpgradeCheckpoint.ReadSource(journal, options, runtime.Apply,
            runtime.Identity.Incarnation, sourceDataEpoch);
        runtime.SetPosition(position);
        var header = new byte[ZoneTreePersistenceFormat.HeaderLength];
        while (journal.Position < journal.Length)
        {
            journal.ReadExactly(header);
            position = ReplayFrame(journal, options, runtime, header, position);
            runtime.SetPosition(position);
        }
        return position;
    }

    private static long ValidateAndReadFrame(FileStream journal, ZoneTreeStoreOptions options, byte[] header,
        long position)
    {
        var frame = ReadHeader(header, position, options.MaxFrameBytes);
        if (journal.Length - journal.Position < frame.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid);
        }
        var payload = new byte[frame.Length];
        journal.ReadExactly(payload);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload),
            header.AsSpan(ZoneTreePersistenceFormat.ChecksumOffset, ZoneTreePersistenceFormat.ChecksumLength)))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalChecksumInvalid);
        }
        ValidateAppliedPosition(ZoneTreeJournalCodec.Deserialize(payload));
        return frame.Sequence;
    }

    private static long ReplayFrame(FileStream journal, ZoneTreeStoreOptions options, ZoneTreeStoreRuntime runtime,
        byte[] header, long position)
    {
        var frame = ReadHeader(header, position, options.MaxFrameBytes);
        if (journal.Length - journal.Position < frame.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid);
        }
        var payload = new byte[frame.Length];
        journal.ReadExactly(payload);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload),
            header.AsSpan(ZoneTreePersistenceFormat.ChecksumOffset, ZoneTreePersistenceFormat.ChecksumLength)))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalChecksumInvalid);
        }
        var mutations = ZoneTreeJournalCodec.Deserialize(payload);
        ValidateAppliedPosition(mutations);
        foreach (var mutation in mutations)
        {
            runtime.Apply(mutation);
        }
        return frame.Sequence;
    }

    private static void ValidateAppliedPosition(StorageMutation[] mutations)
    {
        var appliedKey = KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
            ZoneTreePersistenceFormat.LastAppliedKey);
        foreach (var mutation in mutations)
        {
            if (!mutation.Key.Span.SequenceEqual(appliedKey))
            {
                continue;
            }
            if (mutation.Value is not { } value)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreeJournalCodec.JournalRecordsInvalid);
            }
            long applied;
            try
            {
                applied = NativeSerialization.Deserialize<long>(value.Span);
            }
            catch (Exception error) when (ZoneTreeJournalCodec.IsMalformedPayload(error))
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreeJournalCodec.JournalPayloadInvalid);
            }
            if (applied < InitialAppliedPosition)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreeJournalCodec.JournalRecordsInvalid);
            }
        }
    }

    internal static (int Length, long Sequence) ReadHeader(byte[] header, long position, int maxFrameBytes)
    {
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(header);
        if (IsLegacyJournalMagic(magic))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.JournalFormatUpgradeRequired);
        }
        if (magic != ZoneTreePersistenceFormat.JournalMagic)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalHeaderInvalid);
        }
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(ZoneTreePersistenceFormat.PayloadLengthOffset));
        var sequence = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(ZoneTreePersistenceFormat.SequenceOffset));
        if (length <= EmptyFrameLength || length > maxFrameBytes || position == long.MaxValue || sequence != position + NextJournalSequenceIncrement)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid);
        }
        return (length, sequence);
    }

    private static bool IsLegacyJournalMagic(ulong magic)
        => magic is ZoneTreePersistenceFormat.LegacyJournalMagic
            or ZoneTreePersistenceFormat.LegacyBinaryJournalMagic
            or ZoneTreePersistenceFormat.LegacyNativeMutationJournalMagic;
}
