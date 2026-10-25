using System.Text;

namespace KeyLoad.ProcessRecoveryTests.Search;

internal static class TextCrashMarkerReader
{
    private const int OutputByteLimit = 8_192;
    private const int ErrorCharacterLimit = 1_024;
    private const int LineLimit = 32;
    private const int BufferSize = 256;

    internal static async Task AwaitAsync(StreamReader output, StreamReader error, CancellationToken cancellationToken)
    {
        var buffer = new char[BufferSize];
        var line = new StringBuilder();
        var outputBytes = 0;
        var lineCount = 0;
        while (true)
        {
            var read = await output.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                var diagnostic = await ReadErrorSummaryAsync(error, cancellationToken);
                throw new InvalidOperationException("The execution text process exited before its requested stage marker: " + diagnostic);
            }
            outputBytes += Encoding.UTF8.GetByteCount(buffer.AsSpan(0, read));
            if (outputBytes > OutputByteLimit)
            {
                throw new InvalidOperationException("Execution text process output exceeded the marker wait bound.");
            }
            if (ContainsMarker(buffer.AsSpan(0, read), line, ref lineCount))
            {
                return;
            }
        }
    }

    internal static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[BufferSize];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) != 0)
        {
        }
    }

    private static bool ContainsMarker(ReadOnlySpan<char> chunk, StringBuilder line, ref int lineCount)
    {
        foreach (var character in chunk)
        {
            if (character != '\n')
            {
                line.Append(character);
                continue;
            }
            if (line.Length > 0 && line[^1] == '\r')
            {
                line.Length--;
            }
            var matches = string.Equals(line.ToString(), CrashFixtureValues.CrashMarker, StringComparison.Ordinal);
            line.Clear();
            if (matches)
            {
                return true;
            }
            if (++lineCount > LineLimit)
            {
                throw new InvalidOperationException("Execution text process output exceeded the marker line bound.");
            }
        }
        return false;
    }

    private static async Task<string> ReadErrorSummaryAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[BufferSize];
        var summary = new StringBuilder();
        while (summary.Length < ErrorCharacterLimit)
        {
            var count = Math.Min(buffer.Length, ErrorCharacterLimit - summary.Length);
            var read = await reader.ReadAsync(buffer.AsMemory(0, count), cancellationToken);
            if (read == 0)
            {
                break;
            }
            summary.Append(buffer, 0, read);
        }
        return summary.ToString();
    }
}
