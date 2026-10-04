using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeIdentityFile
{
    internal static StoreIdentity Open(ZoneTreeStoreOptions options, FileStream ownership)
    {
        var path = Path.Combine(options.Directory, IdentityFileName);
        if (File.Exists(path))
        {
            var existing = Read(path);
            Validate(existing, options);
            return existing;
        }

        RequireEmptyOwnedDirectory(options.Directory, ownership);
        var identity = new StoreIdentity(CurrentDataEpoch, KeyCodec.Version,
            Guid.NewGuid(), options.Incarnation ?? Guid.NewGuid(),
            options.SigningKey is { } configuredKey ? configuredKey.ToArray() : RandomNumberGenerator.GetBytes(SigningKeyBytes),
            DurabilityProfile.ProcessDurable);
        Validate(identity, options);
        Write(path, identity);

        return identity;
    }

    private static void RequireEmptyOwnedDirectory(string directory, FileStream ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        var ownerPath = Path.GetFullPath(Path.Combine(directory, OwnerLockFileName));
        if (!ownership.CanRead || !ownership.CanWrite
            || !string.Equals(ownership.Name, ownerPath, StringComparison.Ordinal)
            || ownership.Length != 0
            || (File.GetAttributes(ownerPath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, IdentityFormatUnsupported);
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (!string.Equals(Path.GetFullPath(entry), ownerPath, StringComparison.Ordinal))
            {
                throw Errors.Fail(ErrorCode.FormatUnsupported, IdentityFormatUnsupported);
            }
        }
    }

    internal static StoreIdentity OpenExisting(ZoneTreeStoreOptions options, Guid expectedNodeId)
    {
        var identity = Read(Path.Combine(options.Directory, IdentityFileName));
        Validate(identity, options);
        if (identity.FormatVersion != CurrentDataEpoch)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, IdentityFormatUnsupported);
        }
        if (identity.NodeId != expectedNodeId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, IdentityScopeInvalid);
        }
        return identity;
    }

    private static void Validate(StoreIdentity identity, ZoneTreeStoreOptions options)
    {
        if (identity.FormatVersion != CurrentDataEpoch
            || identity.KeyCodecVersion != KeyCodec.Version)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, IdentityFormatUnsupported);
        }

        if (identity.SigningKey.Length != SigningKeyBytes
            || options.Incarnation is { } incarnation && incarnation != identity.Incarnation
            || options.SigningKey is { } signingKey && !CryptographicOperations.FixedTimeEquals(signingKey.Span, identity.SigningKey.Span))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, IdentityScopeInvalid);
        }
    }

    internal static StoreIdentity Read(string path)
    {
        var bytes = ZoneTreeMetadataFile.Read(path, MaximumIdentityFileBytes, IdentityFormatUnsupported);
        return Read(bytes.Span);
    }

    internal static StoreIdentity Read(ReadOnlySpan<byte> bytes)
    {
        var envelope = ZoneTreeMetadataBinary.Read<ZoneTreeIdentityEnvelope>(bytes, ZoneTreeMetadataBinary.IdentityMagic, IdentityFormatUnsupported);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload), envelope.Checksum))
        {
            throw Errors.Fail(ErrorCode.Corruption, IdentityChecksumInvalid);
        }

        var identity = NativeSerialization.Deserialize<StoreIdentity>(envelope.Payload);
        if (identity.FormatVersion != CurrentDataEpoch || identity.KeyCodecVersion != KeyCodec.Version)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, IdentityFormatUnsupported);
        }

        if (identity.SigningKey.Length != SigningKeyBytes || identity.NodeId == Guid.Empty || identity.Incarnation == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, IdentityChecksumInvalid);
        }

        return identity;
    }

    internal static void Write(string path, StoreIdentity identity)
    {
        var payload = NativeSerialization.Serialize(identity);
        var bytes = ZoneTreeMetadataBinary.Write(new ZoneTreeIdentityEnvelope(payload, SHA256.HashData(payload)), ZoneTreeMetadataBinary.IdentityMagic);
        var temporary = path + TemporaryFileSuffix;
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
            IdentityBufferBytes, FileOptions.WriteThrough))
        {
            file.Write(bytes);
            file.Flush(true);
        }

        File.Move(temporary, path, true);
    }

    internal static StoreIdentity ReadNative5ForUpgrade(ReadOnlySpan<byte> bytes)
    {
        var envelope = ZoneTreeMetadataBinary.Read<ZoneTreeIdentityEnvelope>(bytes,
            ZoneTreeMetadataBinary.IdentityMagic, IdentityFormatUnsupported);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload), envelope.Checksum))
        {
            throw Errors.Fail(ErrorCode.Corruption, IdentityChecksumInvalid);
        }

        var identity = NativeSerialization.Deserialize<StoreIdentity>(envelope.Payload);
        if (identity.FormatVersion != SourceDataEpoch || identity.KeyCodecVersion != KeyCodec.Version)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, IdentityFormatUnsupported);
        }

        ValidateIdentityFields(identity);
        return identity;
    }

    private static void ValidateIdentityFields(StoreIdentity identity)
    {
        if (identity.SigningKey.Length != SigningKeyBytes || identity.NodeId == Guid.Empty || identity.Incarnation == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, IdentityChecksumInvalid);
        }
    }

}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ZoneTreeMetadataAliases.IdentityEnvelope)]
internal sealed record ZoneTreeIdentityEnvelope(
    [property: global::Orleans.Id(0)] byte[] Payload,
    [property: global::Orleans.Id(1)] byte[] Checksum);
