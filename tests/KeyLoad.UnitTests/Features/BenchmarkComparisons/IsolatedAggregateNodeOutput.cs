using System.Text;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed record IsolatedAggregateNodeResult(int ExitCode, string Output, string Error);

internal static class IsolatedAggregateNodeOutput
{
    private const int BufferCharacters = 4096;
    private const int MaximumCharacters = 65_536;
    private const string OutputLimitMessage = "Isolated aggregate child output exceeded its bound.";

    internal static async Task<string> ReadAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[BufferCharacters];
        var text = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
            {
                return text.ToString();
            }

            if (text.Length + count > MaximumCharacters)
            {
                throw new InvalidOperationException(OutputLimitMessage);
            }

            text.Append(buffer, 0, count);
        }
    }

    internal static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The bounded reader was cancelled while the owned Node child was reaped.
        }
        catch (IOException)
        {
            // Killing the owned child can close its redirected pipe.
        }
        catch (InvalidOperationException error) when (error.Message == OutputLimitMessage)
        {
            // The originating output-bound failure remains the caller's failure.
        }
    }
}
