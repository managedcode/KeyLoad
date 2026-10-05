using System.Text;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonResourceLogBuffer(int maximumLines, int maximumBytes, int maximumLineBytes)
{
    private readonly System.Threading.Lock gate = new();
    private readonly LinkedList<RetainedLine> lines = new();
    private readonly LinkedListNode<RetainedLine>?[] protectedLines = new LinkedListNode<RetainedLine>?[ComparisonReplayDiagnosticLog.SlotCount];
    private long retainedBytes;
    private long protectedBytes;

    internal void Add(string line)
    {
        var bounded = Bound(line);
        ComparisonReplayDiagnosticLog? candidate = line == bounded && ComparisonReplayDiagnosticLog.TryRead(line, out var parsed) ? parsed : null;
        lock (gate)
        {
            var diagnostic = MatchConfiguration(candidate);
            if (diagnostic is { } record && protectedLines[record.Key] is { } previous)
            {
                Remove(previous);
            }
            var retained = new RetainedLine(bounded, Encoding.UTF8.GetByteCount(bounded) + 1L, diagnostic);
            var node = lines.AddLast(retained);
            retainedBytes += retained.Bytes;
            if (diagnostic is { } accepted)
            {
                protectedLines[accepted.Key] = node;
                protectedBytes += retained.Bytes;
                if (accepted.Key == 0)
                { DemoteMismatches(accepted); }
            }
            Trim();
        }
    }

    internal string[] Snapshot()
    {
        lock (gate)
        {
            return lines.Select(line => line.Text).ToArray();
        }
    }

    private ComparisonReplayDiagnosticLog? MatchConfiguration(ComparisonReplayDiagnosticLog? candidate)
        => candidate is { Key: > 0 } quota && protectedLines[0]?.Value.Diagnostic is { } configuration
            && !quota.Matches(configuration) ? null : candidate;

    private void DemoteMismatches(ComparisonReplayDiagnosticLog configuration)
    {
        for (var key = 1; key < protectedLines.Length; key++)
        {
            var node = protectedLines[key];
            if (node?.Value.Diagnostic is { } quota && !quota.Matches(configuration))
            {
                protectedBytes -= node.Value.Bytes;
                node.Value = node.Value with { Diagnostic = null };
                protectedLines[key] = null;
            }
        }
    }

    private void Trim()
    {
        while (lines.Count > maximumLines || retainedBytes > maximumBytes)
        {
            Remove(First(protectedOnly: false) ?? lines.First!);
        }
        while (protectedBytes > ComparisonReplayDiagnosticLog.MaximumProtectedBytes)
        {
            Remove(First(protectedOnly: true)!);
        }
    }

    private LinkedListNode<RetainedLine>? First(bool protectedOnly)
    {
        for (var node = lines.First; node is not null; node = node.Next)
        {
            if (node.Value.Diagnostic.HasValue == protectedOnly)
            {
                return node;
            }
        }
        return null;
    }

    private void Remove(LinkedListNode<RetainedLine> node)
    {
        retainedBytes -= node.Value.Bytes;
        if (node.Value.Diagnostic is { } diagnostic)
        {
            protectedBytes -= node.Value.Bytes;
            protectedLines[diagnostic.Key] = null;
        }
        lines.Remove(node);
    }

    private string Bound(string line)
    {
        var characters = 0;
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maximumLineBytes)
            {
                break;
            }
            bytes += rune.Utf8SequenceLength;
            characters += rune.Utf16SequenceLength;
        }
        return line[..characters];
    }

    private sealed record RetainedLine(string Text, long Bytes, ComparisonReplayDiagnosticLog? Diagnostic);
}
