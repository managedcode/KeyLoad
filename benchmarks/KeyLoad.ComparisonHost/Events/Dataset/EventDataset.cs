using System.Buffers.Binary;
using System.Globalization;

namespace KeyLoad.ComparisonHost.Events;

/// <summary>An event that starts its own stream.</summary>
internal sealed record EventRecord(string Stream, Guid EventId, string Type, string Json);

/// <summary>The standard event dataset: one stream <c>s#########</c> per event, with a deterministic id and a 1 KiB JSON body.</summary>
internal static class EventDataset
{
    internal const string Type = "benchmark-event";
    internal const int BodyBytes = 1024;
    private const string StreamPrefix = "s";
    private const string Format = "D9";
    private const string JsonPrefix = "{\"stream\":\"";
    private const string JsonPadding = "\",\"padding\":\"";
    private const string JsonSuffix = "\"}";
    private const char Padding = 'x';
    private const int GuidBytes = 16;

    internal static string Stream(int number) => StreamPrefix + number.ToString(Format, CultureInfo.InvariantCulture);

    internal static EventRecord Create(int number, int seed)
    {
        var stream = Stream(number);
        var head = JsonPrefix + stream + JsonPadding;
        return new(stream, EventId(number, seed), Type, head + new string(Padding, BodyBytes - head.Length - JsonSuffix.Length) + JsonSuffix);
    }

    internal static Guid EventId(int number, int seed)
    {
        Span<byte> bytes = stackalloc byte[GuidBytes];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, seed);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[8..], number);
        return new(bytes);
    }

    internal static bool Matches(EventRecord actual, int number, int seed)
    {
        var expected = Create(number, seed);
        return actual.Stream == expected.Stream && actual.EventId == expected.EventId
            && string.Equals(actual.Type, expected.Type, StringComparison.Ordinal);
    }
}
