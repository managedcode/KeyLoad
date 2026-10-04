using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class KeyCodecMixedCorpus
{
    internal const int Count = 10_000;
    private const int Seed = 0x51A7;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal static KeyCodecCorpusEntry[] Create()
    {
        var entries = new KeyCodecCorpusEntry[Count];
        for (var index = 0; index < entries.Length; index++)
        {
            var componentCount = index % 4;
            var components = new object?[componentCount];
            for (var component = 0; component < components.Length; component++)
            {
                components[component] = CreateValue(index + component * 7);
            }

            entries[index] = new(index, components);
        }

        AddIdentityAndPrefixCases(entries);
        return entries;
    }

    private static object? CreateValue(int selector) => (selector % 10) switch
    {
        0 => MissingValue.Instance,
        1 => null,
        2 => selector / 10 % 2 == 1,
        3 => selector % 3 == 0 ? (object)(int.MinValue + selector) : ReadInteger(selector),
        4 => CreateDecimal(selector),
        5 => (long)(ReadEntropy(selector) % 2_000_000) / 100d - 10_000d,
        6 => CreateTimestamp(selector),
        7 => CreateString(selector),
        8 => GuidFrom(selector),
        _ => CreateBytes(selector)
    };

    private static decimal CreateDecimal(int selector)
    {
        var value = (long)(ReadEntropy(selector) % 18_000_000_000) - 9_000_000_000;
        var scale = selector % 9;
        return value / Pow10(scale);
    }

    private static long ReadInteger(int selector) => unchecked((long)ReadEntropy(selector));

    private static ulong ReadEntropy(int selector) => BinaryPrimitives.ReadUInt64LittleEndian(CreateDigest(selector));

    private static decimal Pow10(int scale)
    {
        var value = 1m;
        for (var index = 0; index < scale; index++)
        {
            value *= 10m;
        }

        return value;
    }

    private static DateTimeOffset CreateTimestamp(int selector)
    {
        var utc = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(selector * 1_337L);
        return utc.ToOffset(TimeSpan.FromMinutes((selector % 25 - 12) * 15));
    }

    private static string CreateString(int selector) => (selector % 3) switch
    {
        0 => $"key-{selector:D6}",
        1 => $"κλειδί-{selector % 1_000:D4}",
        _ => $"emoji-😀-{selector % 1_000:D4}"
    };

    private static Guid GuidFrom(int selector)
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(bytes, selector);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[8..], (long)selector * 17);
        return new(bytes);
    }

    private static byte[] CreateBytes(int selector) => CreateDigest(selector)[..(selector % 7)];

    private static byte[] CreateDigest(int selector)
    {
        Span<byte> input = stackalloc byte[sizeof(int) * 2];
        BinaryPrimitives.WriteInt32LittleEndian(input, Seed);
        BinaryPrimitives.WriteInt32LittleEndian(input[sizeof(int)..], selector);
        return SHA256.HashData(input);
    }

    internal static object? Normalize(object? component) => component switch
    {
        int value => (long)value,
        Guid value => value.ToString("N", CultureInfo.InvariantCulture),
        DateTimeOffset value => value.ToUniversalTime(),
        double value when value == 0 => 0d,
        _ => component
    };

    internal static int Compare(KeyCodecCorpusEntry left, KeyCodecCorpusEntry right)
    {
        var shared = Math.Min(left.Components.Length, right.Components.Length);
        for (var index = 0; index < shared; index++)
        {
            var comparison = CompareComponent(left.Components[index], right.Components[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Components.Length.CompareTo(right.Components.Length);
    }

    private static int CompareComponent(object? left, object? right)
    {
        var typeOrder = TypeOrder(left).CompareTo(TypeOrder(right));
        if (typeOrder != 0)
        {
            return typeOrder;
        }

        return (left, right) switch
        {
            (MissingValue, MissingValue) or (null, null) => 0,
            (bool first, bool second) => first.CompareTo(second),
            (int first, int second) => first.CompareTo(second),
            (int first, long second) => ((long)first).CompareTo(second),
            (long first, int second) => first.CompareTo((long)second),
            (long first, long second) => first.CompareTo(second),
            (decimal first, decimal second) => first.CompareTo(second),
            (double first, double second) => NormalizeDouble(first).CompareTo(NormalizeDouble(second)),
            (DateTimeOffset first, DateTimeOffset second) => first.UtcTicks.CompareTo(second.UtcTicks),
            (Guid first, Guid second) => CompareUtf8(first.ToString("N"), second.ToString("N")),
            (Guid first, string second) => CompareUtf8(first.ToString("N"), second),
            (string first, Guid second) => CompareUtf8(first, second.ToString("N")),
            (string first, string second) => CompareUtf8(first, second),
            (byte[] first, byte[] second) => first.AsSpan().SequenceCompareTo(second),
            _ => throw new InvalidOperationException("Corpus contains incompatible semantic types.")
        };
    }

    private static int TypeOrder(object? value) => value switch
    {
        MissingValue => 10,
        null => 11,
        bool => 20,
        int or long => 30,
        decimal => 31,
        double => 32,
        DateTimeOffset => 40,
        string or Guid => 50,
        byte[] => 60,
        _ => throw new InvalidOperationException("Corpus contains an unsupported semantic type.")
    };

    private static double NormalizeDouble(double value) => value == 0 ? 0d : value;

    private static int CompareUtf8(string left, string right)
        => Utf8.GetBytes(left).AsSpan().SequenceCompareTo(Utf8.GetBytes(right));

    private static void AddIdentityAndPrefixCases(KeyCodecCorpusEntry[] entries)
    {
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        entries[1] = new(1, [id]);
        entries[2] = new(2, [id.ToString("N")]);
        entries[3] = new(3, [17]);
        entries[4] = new(4, [17L]);
        entries[5] = new(5, [1.00m]);
        entries[6] = new(6, [1m]);
        entries[7] = new(7, [-0d]);
        entries[8] = new(8, [0d]);
        entries[9] = new(9, ["prefix"]);
        entries[10] = new(10, ["prefix", null]);
        entries[11] = new(11, ["prefix", null, null]);
    }
}

internal sealed record KeyCodecCorpusEntry(int Index, object?[] Components);
