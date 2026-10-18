using System.Text;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class NativeToolProcessOutput
{
    internal static async Task<string> ReadAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[NativeExecutionPolicyFixture.Harness().Value.NativeToolReadBufferCharacters];
        var text = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
            {
                return text.ToString();
            }

            if (text.Length + count > NativeExecutionPolicyFixture.Harness().Value.NativeToolMaximumOutputCharacters)
            {
                throw new InvalidOperationException(IsolatedGitHubNativeProtocol.Failure);
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
            // Cancelling the owned child closes its bounded readers.
        }
        catch (IOException)
        {
            // Reaping the owned child can close its redirected pipe.
        }
        catch (InvalidOperationException error) when (error.Message == IsolatedGitHubNativeProtocol.Failure)
        {
            // The original bound failure remains the test failure.
        }
    }
}
