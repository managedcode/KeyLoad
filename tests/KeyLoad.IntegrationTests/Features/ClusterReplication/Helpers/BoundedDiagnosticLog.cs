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

    /// <summary>Formats each actual node's original status, priority failures and newest tail before fair clipping.</summary>
    internal static string[] FormatOwnedNodeLogs(IEnumerable<string> names,
        IEnumerable<NativeLogSubscriptionEvidence> evidence, Func<string, string[]> readFailures,
        Func<string, string[]> readTail)
        => BoundOwnedNodeLogs(names.Select(name => (
            Head: evidence.Where(entry => entry.NodeName == name).OrderByDescending(entry => entry.StartedAt)
                .Take(1).Select(entry => entry.Snapshot()).DefaultIfEmpty(name + ": native log subscription Unobserved.")
                .Concat(readFailures(name).Select(line => name + ": " + line)),
            Tail: readTail(name).Reverse().Select(line => name + ": " + line))));

    /// <summary>Reserves head and tail shares for every actual owned node within the unchanged total limits.</summary>
    internal static string[] BoundOwnedNodeLogs(IEnumerable<(IEnumerable<string> Head, IEnumerable<string> Tail)> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var groups = nodes.ToArray();
        if (groups.Length == 0)
        {
            return [];
        }
        if (groups.Length > ClusterFixtureProtocol.NodeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(nodes));
        }
        var nodeLines = MaximumLines / groups.Length;
        var nodeBytes = MaximumUtf8Bytes / groups.Length;
        var headLines = nodeLines / 2;
        var headBytes = nodeBytes / 2;
        var result = new List<string>();
        foreach (var group in groups)
        {
            result.AddRange(BoundCore(group.Head, headLines, headBytes));
            result.AddRange(BoundCore(group.Tail, nodeLines - headLines, nodeBytes - headBytes));
        }
        return result.ToArray();
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
