using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class RuntimeJournalValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Name(string value, RuntimeJournalOptions options)
    {
        if (string.IsNullOrEmpty(value) || Utf8Length(value) > options.MaximumNameBytes)
        {
            Invalid();
        }
    }

    internal static void Metadata(IReadOnlyDictionary<string, string> properties, RuntimeJournalOptions options)
    {
        if (properties.Count > options.MaximumMetadataEntries)
        {
            Capacity();
        }

        var bytes = RuntimeJournalProtocol.EmptyCount;
        foreach (var pair in properties)
        {
            if (pair.Key is null || pair.Value is null)
            {
                Invalid();
            }
            var keyLength = Utf8Length(pair.Key);
            var valueLength = Utf8Length(pair.Value);
            if (keyLength == RuntimeJournalProtocol.EmptyCount || keyLength > options.MaximumNameBytes
                || valueLength > options.MaximumNameBytes)
            {
                Invalid();
            }
            bytes = checked(bytes + keyLength + valueLength);
            if (bytes > options.MaximumMetadataBytes)
            {
                Capacity();
            }
        }
        _ = EffectivePoison(properties);
    }

    internal static void MetadataKey(string key, RuntimeJournalOptions options)
    {
        var length = Utf8Length(key);
        if (length == RuntimeJournalProtocol.EmptyCount || length > options.MaximumNameBytes)
        {
            Invalid();
        }
    }

    internal static long EffectiveOwnerGeneration(IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        var changedOwner = !string.Equals(EffectiveOwner(before), EffectiveOwner(after), StringComparison.Ordinal);
        var changedPoison = EffectivePoison(before) != EffectivePoison(after);
        return changedOwner || changedPoison
            ? RuntimeJournalProtocol.OwnerGenerationIncrement
            : RuntimeJournalProtocol.EmptyGeneration;
    }

    internal static byte[] StrictBytes(string value)
    {
        try
        {
            return StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Validation, Contracts.RuntimeJournalProtocol.InvalidRequest);
        }
    }

    internal static string NextMetadataETag(string previous, IReadOnlyDictionary<string, string> properties)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true))
        {
            WriteField(writer, previous);
            writer.Write(properties.Count);
            foreach (var pair in properties.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                WriteField(writer, pair.Key);
                WriteField(writer, pair.Value);
            }
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteField(BinaryWriter writer, string value)
    {
        var bytes = StrictBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static int Utf8Length(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            return StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Validation, Contracts.RuntimeJournalProtocol.InvalidRequest);
        }
    }

    private static string? EffectiveOwner(IReadOnlyDictionary<string, string> properties)
        => properties.TryGetValue(Contracts.RuntimeJournalProtocol.OwnerProperty, out var owner)
            && owner.Length != RuntimeJournalProtocol.EmptyCount ? owner : null;

    private static bool EffectivePoison(IReadOnlyDictionary<string, string> properties)
    {
        if (!properties.TryGetValue(Contracts.RuntimeJournalProtocol.PoisonedProperty, out var value))
        {
            return false;
        }
        if (bool.TryParse(value, out var poisoned))
        {
            return poisoned;
        }
        throw Errors.Fail(ErrorCode.RecoveryRequired, Contracts.RuntimeJournalProtocol.InvalidState);
    }

    internal static bool IsValidMetadataETag(string? value, Guid instanceId)
    {
        if (value is null)
        {
            return false;
        }
        return value.Length switch
        {
            RuntimeJournalProtocol.InitialMetadataETagLength => Guid.TryParseExact(value,
                RuntimeJournalProtocol.InitialMetadataETagFormat, out var parsed) && parsed == instanceId && HasOnlyLowerHex(value),
            RuntimeJournalProtocol.ChainedMetadataETagLength => HasOnlyLowerHex(value),
            _ => false
        };
    }

    private static bool HasOnlyLowerHex(string value) => value.All(char.IsAsciiHexDigitLower);

    [DoesNotReturn]
    private static void Invalid() => throw Errors.Fail(ErrorCode.Validation, Contracts.RuntimeJournalProtocol.InvalidRequest);

    private static void Capacity() => throw Errors.Fail(ErrorCode.ResourceExhausted, Contracts.RuntimeJournalProtocol.Capacity);
}
