using System.Text;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Applies the fixture's unchanged final diagnostic line and UTF-8 byte limits.</summary>
internal static class BoundedDiagnosticLog
{
    private const int MaximumLines = 80;
    private const int MaximumUtf8Bytes = 8_192;

    /// <summary>Returns a prefix clipped to the existing line and encoded-byte budgets.</summary>
    /// <param name="source">The ordered resource, discovery and log lines.</param>
    /// <returns>A bounded sequence suitable for the RF3 diagnostic artifact.</returns>
    internal static string[] Bound(IEnumerable<string> source)
        => BoundCore(source, MaximumLines, MaximumUtf8Bytes);

    /// <summary>Reserves an equal unchanged-budget share for each RF3 node before concatenation.</summary>
    internal static string[] BoundNodes(IEnumerable<IEnumerable<string>> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return Bound(nodes.SelectMany(node => BoundCore(node, MaximumLines / ClusterFixtureProtocol.NodeCount,
            MaximumUtf8Bytes / ClusterFixtureProtocol.NodeCount)));
    }

    private static string[] BoundCore(IEnumerable<string> source, int maximumLines, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        var newlineBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);
        var lines = new List<string>(maximumLines);
        var bytes = 0;
        foreach (var original in source)
        {
            if (lines.Count == maximumLines)
            {
                break;
            }

            var remaining = maximumBytes - bytes;
            if (remaining <= newlineBytes)
            {
                break;
            }

            var line = ClipUtf8(original, remaining - newlineBytes);
            bytes += Encoding.UTF8.GetByteCount(line) + newlineBytes;
            lines.Add(line);
        }

        return lines.ToArray();
    }

    internal static string ClipUtf8(string value, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
        {
            return value;
        }

        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maximumBytes)
            {
                break;
            }

            builder.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }

        return builder.ToString();
    }
}
