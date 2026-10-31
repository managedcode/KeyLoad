using System.Globalization;

namespace KeyLoad.ComparisonHost.KeyValue;

/// <summary>One key and its value of the standard key-value dataset.</summary>
internal sealed record KeyValueRecord(string Key, string Value);

/// <summary>The standard key-value dataset: <c>k#########</c> keys with 1 KiB ASCII values.</summary>
internal static class KeyValueDataset
{
    internal const int ValueBytes = 1024;
    private const string KeyPrefix = "k";
    private const string ValuePrefix = "v";
    private const string Format = "D9";
    private const char Separator = '.';
    private const char Padding = 'x';

    internal static string Key(int number) => KeyPrefix + number.ToString(Format, CultureInfo.InvariantCulture);

    internal static KeyValueRecord Create(int number, int version = 0)
    {
        var head = Head(number) + version.ToString(CultureInfo.InvariantCulture) + Separator;
        return new(Key(number), head + new string(Padding, ValueBytes - head.Length));
    }

    /// <summary>Checks that a returned value belongs to this key, whatever its version.</summary>
    internal static bool Matches(string value, int number)
        => value.Length == ValueBytes && value.StartsWith(Head(number), StringComparison.Ordinal);

    private static string Head(int number) => ValuePrefix + number.ToString(Format, CultureInfo.InvariantCulture) + Separator;
}
