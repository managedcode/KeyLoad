using System.Text;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

internal sealed class ComparisonResourceLogBuffer(int maximumLines, int maximumBytes, int maximumLineBytes)
{
    private const long SingleLongCount = 1L;
    private const int EmptyCount = 0;

    private readonly System.Threading.Lock gate = new();
    private readonly Queue<RetainedLine> lines = new();
    private long retainedBytes;

    internal void Add(string line)
    {
        var bounded = Bound(line);
        var retained = new RetainedLine(bounded, Encoding.UTF8.GetByteCount(bounded) + SingleLongCount);
        lock (gate)
        {
            lines.Enqueue(retained);
            retainedBytes += retained.Bytes;
            while (lines.Count > maximumLines || retainedBytes > maximumBytes)
            {
                retainedBytes -= lines.Dequeue().Bytes;
            }
        }
    }

    internal string[] Snapshot()
    {
        lock (gate)
        {
            return lines.Select(line => line.Text).ToArray();
        }
    }

    private string Bound(string line)
    {
        var characters = EmptyCount;
        var bytes = EmptyCount;
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

    private sealed record RetainedLine(string Text, long Bytes);
}
