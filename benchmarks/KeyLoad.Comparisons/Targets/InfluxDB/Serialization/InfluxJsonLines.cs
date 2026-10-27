using System.Buffers;
using System.IO.Pipelines;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class InfluxJsonLines
{
    private const byte Newline = (byte)'\n';
    private const string Id = "id", Body = "body";
    private const string TooLarge = "InfluxReadbackRowLimitExceeded", Truncated = "InfluxReadbackRowTruncated";

    internal static async IAsyncEnumerable<FoundDocument> ReadAsync(Stream input, ComparisonExecutionOptions policy,
        CancellationTokenSource deadline)
    {
        var reader = PipeReader.Create(input, new StreamPipeReaderOptions(bufferSize: policy.ReadBufferBytes,
            minimumReadSize: policy.ReadBufferBytes, leaveOpen: true));
        try
        {
            while (true)
            {
                deadline.CancelAfter(policy.OperationTimeout);
                var result = await reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                var pending = result.Buffer;
                while (pending.PositionOf(Newline) is { } end)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    var line = pending.Slice(pending.Start, end);
                    RequireLength(line.Length, policy.MaxResponseBytes);
                    pending = pending.Slice(pending.GetPosition(1, end));
                    yield return Parse(line);
                }
                RequireLength(pending.Length, policy.MaxResponseBytes);
                if (result.IsCompleted)
                {
                    RequireComplete(pending);
                }
                reader.AdvanceTo(pending.Start, pending.End);
                if (result.IsCompleted)
                {
                    yield break;
                }
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static FoundDocument Parse(ReadOnlySequence<byte> line)
    {
        using var json = JsonDocument.Parse(line);
        return new(json.RootElement.GetProperty(Id).GetString()!, json.RootElement.GetProperty(Body).GetString()!);
    }

    private static void RequireLength(long length, int maximum)
    {
        if (length > maximum)
        {
            throw new ComparisonFailureException(TooLarge);
        }
    }

    private static void RequireComplete(ReadOnlySequence<byte> remaining)
    {
        if (!remaining.IsEmpty)
        {
            throw new ComparisonFailureException(Truncated);
        }
    }
}
