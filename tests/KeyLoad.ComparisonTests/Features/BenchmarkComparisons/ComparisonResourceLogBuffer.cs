using System.Text;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonResourceLogBuffer(int maximumLines, int maximumBytes, int maximumLineBytes)
{
    private readonly object gate = new();
    private readonly Queue<string> lines = new();
    private int retainedBytes;

    internal void Add(string line)
    {
        var bounded = Bound(line);
        lock (gate)
        {
            lines.Enqueue(bounded);
            retainedBytes += Encoding.UTF8.GetByteCount(bounded) + 1;
            while (lines.Count > maximumLines || retainedBytes > maximumBytes)
            {
                retainedBytes -= Encoding.UTF8.GetByteCount(lines.Dequeue()) + 1;
            }
        }
    }

    internal string[] Snapshot()
    {
        lock (gate)
        {
            return lines.ToArray();
        }
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
}
