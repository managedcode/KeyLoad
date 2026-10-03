using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeIdentityFile
{
    internal static StoreIdentity Open(ZoneTreeStoreOptions options)
    {
        var path = Path.Combine(options.Directory, IdentityFileName);
        var identity = File.Exists(path) ? Read(path) : new(BinaryJournalIdentityVersion, KeyCodec.Version,
            Guid.NewGuid(), options.Incarnation ?? Guid.NewGuid(),
            options.SigningKey is { } configuredKey ? configuredKey.ToArray() : RandomNumberGenerator.GetBytes(SigningKeyBytes),
            DurabilityProfile.ProcessDurable);
        Validate(identity, options);
        if (!File.Exists(path))
        {
            Write(path, identity);
        }

        return identity;
    }

    internal static StoreIdentity Promote(string directory, StoreIdentity identity)
    {
        if (identity.FormatVersion == BinaryJournalIdentityVersion)
        {
            return identity;
        }

        var promoted = identity with { FormatVersion = BinaryJournalIdentityVersion };
        Write(Path.Combine(directory, IdentityFileName), promoted);
        return promoted;
    }

    private static void Validate(StoreIdentity identity, ZoneTreeStoreOptions options)
    {
        if (identity.FormatVersion is not (InitialIdentityVersion or CheckpointVersion or LegacyBinaryJournalIdentityVersion
            or BinaryJournalIdentityVersion)
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
        var envelope = JsonDefaults.Deserialize<ZoneTreeIdentityEnvelope>(bytes);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload), envelope.Checksum))
        {
            throw Errors.Fail(ErrorCode.Corruption, IdentityChecksumInvalid);
        }

        return JsonDefaults.Deserialize<StoreIdentity>(envelope.Payload);
    }

    internal static void Write(string path, StoreIdentity identity)
    {
        var payload = JsonDefaults.Serialize(identity);
        var bytes = JsonDefaults.Serialize(new ZoneTreeIdentityEnvelope(payload, SHA256.HashData(payload)));
        var temporary = path + TemporaryFileSuffix;
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
            IdentityBufferBytes, FileOptions.WriteThrough))
        {
            file.Write(bytes);
            file.Flush(true);
        }

        File.Move(temporary, path, true);
    }

}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ZoneTreeMetadataAliases.IdentityEnvelope)]
internal sealed record ZoneTreeIdentityEnvelope(
    [property: global::Orleans.Id(0)] byte[] Payload,
    [property: global::Orleans.Id(1)] byte[] Checksum);
