using System.Collections.Immutable;
using System.Text;
using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class RuntimeJournalStorageValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static ImmutableDictionary<string, string> CopyProperties(
        IReadOnlyDictionary<string, string>? properties, RuntimeJournalOptions options)
    {
        var values = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (properties is null)
        {
            return values.ToImmutable();
        }

        var bytes = RuntimeJournalStoragePolicy.InitialMetadataByteCount;
        foreach (var (key, value) in properties)
        {
            if (values.Count >= options.MaximumMetadataEntries)
            {
                Capacity();
            }

            ValidateProperty(key, value, options);
            bytes = checked(bytes + ByteCount(key) + ByteCount(value));
            if (bytes > options.MaximumMetadataBytes)
            {
                Capacity();
            }
            values.Add(key, value);
        }

        return values.ToImmutable();
    }

    internal static ImmutableArray<string> CopyRemovals(IEnumerable<string>? properties,
        ImmutableDictionary<string, string> set, RuntimeJournalOptions options)
    {
        if (properties is null)
        {
            return [];
        }

        var values = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var bytes = RuntimeJournalStoragePolicy.InitialMetadataByteCount;
        foreach (var key in properties)
        {
            if (values.Count >= options.MaximumMetadataEntries)
            {
                Capacity();
            }

            ValidatePropertyName(key, options);
            bytes = checked(bytes + ByteCount(key));
            if (bytes > options.MaximumMetadataBytes)
            {
                Capacity();
            }
            if (set.ContainsKey(key))
            {
                throw new ArgumentException(RuntimeJournalStoragePolicy.InvalidMetadata, nameof(properties));
            }

            if (seen.Add(key))
            {
                values.Add(key);
            }
        }

        return values.ToImmutable();
    }

    internal static void ValidateProperty(string key, string value, RuntimeJournalOptions options)
    {
        ValidatePropertyName(key, options);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > options.MaximumNameBytes || ByteCount(value) > options.MaximumNameBytes)
        {
            Invalid();
        }
    }

    internal static void ValidateName(string value, RuntimeJournalOptions options)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > options.MaximumNameBytes
            || value.IndexOf(RuntimeJournalStoragePolicy.InvalidMetadataKeyCharacter, StringComparison.Ordinal)
                > RuntimeJournalStoragePolicy.MissingCharacterIndex
            || ByteCount(value) > options.MaximumNameBytes)
        {
            Invalid();
        }
    }

    private static void ValidatePropertyName(string key, RuntimeJournalOptions options)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith(RuntimeJournalStoragePolicy.ProviderOwnedMetadataPrefix)
            || key.IndexOf(RuntimeJournalStoragePolicy.InvalidMetadataKeyCharacter, StringComparison.Ordinal)
                > RuntimeJournalStoragePolicy.MissingCharacterIndex
            || key.Length > options.MaximumNameBytes
            || ByteCount(key) > options.MaximumNameBytes)
        {
            Invalid();
        }
    }

    private static int ByteCount(string value)
    {
        try
        {
            return StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            Invalid();
            return default;
        }
    }

    private static void Invalid()
        => throw Errors.Fail(ErrorCode.Validation, RuntimeJournalStoragePolicy.InvalidMetadata);

    private static void Capacity()
        => throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalStoragePolicy.InvalidMetadata);
}
