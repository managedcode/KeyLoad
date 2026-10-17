using System.Globalization;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal readonly record struct ComparisonReplayDiagnosticLog(int Key, int Voters, long Critical, long Forward,
    long Read, long Data, long NodeMaximum)
{
    internal const int SlotCount = 13;
    internal static int MaximumProtectedBytes => NativeExecutionPolicyFixture.Harness().Value.MaximumProtectedReplayLogBytes;
    internal static bool TryRead(string original, out ComparisonReplayDiagnosticLog record)
        => ComparisonReplayDiagnosticLogReader.TryRead(original, out record);

    internal bool Matches(ComparisonReplayDiagnosticLog configuration) => Key != 0 && configuration.Key == 0
        && Voters == configuration.Voters && NodeMaximum == configuration.NodeMaximum
        && Critical <= configuration.Critical && Forward <= configuration.Forward && Read <= configuration.Read
        && Data <= configuration.Data && Selected((Key - 1) % 4) == configuration.Selected((Key - 1) % 4);

    private long Selected(int pool) => pool switch
    {
        0 => Critical,
        1 => Forward,
        2 => Read,
        3 => Data,
        _ => throw new ArgumentOutOfRangeException(nameof(pool))
    };
}

internal static class ComparisonReplayDiagnosticLogReader
{
    private const int TimestampCharacters = 28;
    private static int MaximumOriginalCharacters => NativeExecutionPolicyFixture.Harness().Value.MaximumOriginalReplayLogCharacters;
    private const long MaximumNonces = 1_048_576;
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private const string TimestampShape = "0000-00-00T00:00:00.0000000Z";
    private const string Indent = "       ";
    private const string Configured = "ReplicaReplayConfigured ";
    private const string Capacity = "ReplicaReplayCapacity ";
    private const string Voters = "voters=", Critical = "critical=", Forward = "forward=", Read = "read=", Data = "data=";
    private const string NodeMaximum = "nodeMaximum=", Sender = "senderIndex=", Pool = "pool=", Method = "method=";
    private const string Limit = "capacity=", VoterMaximum = "voterMaximum=", Observed = "observedUnixMs=";
    private const string Oldest = "oldestExpiryUnixMs=", Suppressed = "suppressed=";
    private static readonly string[] ConfigurationFields = [Voters, Critical, Forward, Read, Data, NodeMaximum];
    private static readonly string[] CapacityFields = [Sender, Pool, Method, Critical, Forward, Read, Data, Limit,
        VoterMaximum, NodeMaximum, Observed, Oldest, Suppressed];

    internal static bool TryRead(string original, out ComparisonReplayDiagnosticLog record)
    {
        record = default;
        var line = original.AsSpan();
        if (line.Length < TimestampCharacters + Indent.Length || line.Length > MaximumOriginalCharacters
            || !line.Slice(TimestampCharacters, Indent.Length).SequenceEqual(Indent))
        {
            return false;
        }
        var body = line[(TimestampCharacters + Indent.Length)..];
        var configured = body.StartsWith(Configured, StringComparison.Ordinal);
        if ((!configured && !body.StartsWith(Capacity, StringComparison.Ordinal))
            || !Timestamp(line[..TimestampCharacters]))
        {
            return false;
        }
        var fields = configured ? ConfigurationFields : CapacityFields;
        body = body[(configured ? Configured.Length : Capacity.Length)..];
        Span<long> values = stackalloc long[CapacityFields.Length];
        if (!Numbers(body, fields, values))
        {
            return false;
        }
        return configured ? Configuration(values, out record) : Quota(values, out record);
    }

    private static bool Timestamp(ReadOnlySpan<char> value)
    {
        for (var index = 0; index < TimestampShape.Length; index++)
        {
            if (TimestampShape[index] == '0' ? !char.IsAsciiDigit(value[index]) : value[index] != TimestampShape[index])
            {
                return false;
            }
        }
        return DateTimeOffset.TryParseExact(value, TimestampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _);
    }

    private static bool Numbers(ReadOnlySpan<char> body, string[] fields, Span<long> values)
    {
        for (var index = 0; index < fields.Length; index++)
        {
            if (!body.StartsWith(fields[index], StringComparison.Ordinal))
            {
                return false;
            }
            body = body[fields[index].Length..];
            var end = index == fields.Length - 1 ? body.Length : body.IndexOf(' ');
            if (end < 1 || (end > 1 && body[0] == '0')
                || !AsciiDigits(body[..end])
                || !long.TryParse(body[..end], NumberStyles.None, CultureInfo.InvariantCulture, out values[index]))
            {
                return false;
            }
            body = index == fields.Length - 1 ? body[end..] : body[(end + 1)..];
        }
        return body.IsEmpty;
    }

    private static bool AsciiDigits(ReadOnlySpan<char> token)
    {
        foreach (var character in token)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }
        return true;
    }

    private static bool Configuration(ReadOnlySpan<long> values, out ComparisonReplayDiagnosticLog record)
    {
        record = default;
        if (values[0] is < 1 or > 3)
        {
            return false;
        }
        foreach (var value in values[1..ConfigurationFields.Length])
        {
            if (value is < 1 or > MaximumNonces)
            {
                return false;
            }
        }
        if ((values[1] + values[2] + values[3] + values[4]) * values[0] != values[5])
        {
            return false;
        }
        record = new(0, (int)values[0], values[1], values[2], values[3], values[4], values[5]);
        return true;
    }

    private static bool Quota(ReadOnlySpan<long> values, out ComparisonReplayDiagnosticLog record)
    {
        record = default;
        if (values[0] > 2 || values[1] > 3 || values[2] > 8)
        {
            return false;
        }
        foreach (var value in values[3..10])
        {
            if (value > MaximumNonces)
            {
                return false;
            }
        }
        if (values[7] == 0 || values[8] == 0 || values[9] < values[8] || values[9] % values[8] != 0)
        {
            return false;
        }
        var voters = values[9] / values[8];
        if (voters > 3 || values[0] >= voters || values[3] + values[4] + values[5] + values[6] > values[8]
            || values[3 + (int)values[1]] != values[7])
        {
            return false;
        }
        record = new(1 + (int)values[0] * 4 + (int)values[1], (int)voters, values[3], values[4], values[5], values[6], values[9]);
        return true;
    }
}
