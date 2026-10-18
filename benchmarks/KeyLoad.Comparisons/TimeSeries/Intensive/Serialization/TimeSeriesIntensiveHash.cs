using System.Buffers.Binary;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal readonly record struct TimeSeriesIntensiveHash(ulong First, ulong Second, ulong Third, ulong Fourth)
{
    internal static TimeSeriesIntensiveHash Parse(string hex)
    {
        const int FirstElementIndex = 0;
        const int AdjacentElementOffset = 1;

        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != TimeSeriesIntensiveRuntimePolicy.HashBytes * TimeSeriesIntensiveRuntimePolicy.HexCharactersPerByte)
        {
            throw new FormatException(TimeSeriesIntensiveRuntimeErrors.InvalidHash);
        }

        Span<byte> bytes = stackalloc byte[TimeSeriesIntensiveRuntimePolicy.HashBytes];
        for (var index = FirstElementIndex; index < bytes.Length; index++)
        {
            var first = index * TimeSeriesIntensiveRuntimePolicy.HexCharactersPerByte;
            bytes[index] = (byte)(Nibble(hex[first]) * TimeSeriesIntensiveRuntimePolicy.HexRadix + Nibble(hex[first + AdjacentElementOffset]));
        }

        var width = TimeSeriesIntensiveRuntimePolicy.HashWordBytes;
        return new(BinaryPrimitives.ReadUInt64BigEndian(bytes), BinaryPrimitives.ReadUInt64BigEndian(bytes[width..]),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[(width + width)..]), BinaryPrimitives.ReadUInt64BigEndian(bytes[(bytes.Length - width)..]));
    }

    internal string ToHex()
    {
        Span<byte> bytes = stackalloc byte[TimeSeriesIntensiveRuntimePolicy.HashBytes];
        var width = TimeSeriesIntensiveRuntimePolicy.HashWordBytes;
        BinaryPrimitives.WriteUInt64BigEndian(bytes, First);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[width..], Second);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[(width + width)..], Third);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[(bytes.Length - width)..], Fourth);
        return Convert.ToHexStringLower(bytes);
    }

    private static int Nibble(char value) => value switch
    {
        >= TimeSeriesIntensiveRuntimePolicy.DigitStart and <= TimeSeriesIntensiveRuntimePolicy.DigitEnd => value - TimeSeriesIntensiveRuntimePolicy.DigitStart,
        >= TimeSeriesIntensiveRuntimePolicy.LowerStart and <= TimeSeriesIntensiveRuntimePolicy.HexLowerEnd => value - TimeSeriesIntensiveRuntimePolicy.LowerStart + TimeSeriesIntensiveRuntimePolicy.HexLetterOffset,
        >= TimeSeriesIntensiveRuntimePolicy.UpperStart and <= TimeSeriesIntensiveRuntimePolicy.HexUpperEnd => value - TimeSeriesIntensiveRuntimePolicy.UpperStart + TimeSeriesIntensiveRuntimePolicy.HexLetterOffset,
        _ => throw new FormatException(TimeSeriesIntensiveRuntimeErrors.InvalidHash)
    };
}
