using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OpenLoopPlanOutputCapture
{
    private const string FailureMessage = "The open-loop plan Node process failed.";
    private readonly OpenLoopPlanProcessOptions settings;
    private readonly StringBuilder output = new();

    internal OpenLoopPlanOutputCapture(IOptions<OpenLoopPlanProcessOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        settings = executionOptions.Value;
        settings.Validate();
    }

    internal string CapturedOutput => output.ToString();
    internal bool ExceededBound { get; private set; }

    internal async Task<string> ReadAsync(StreamReader reader, TaskCompletionSource? ready)
    {
        var buffer = new char[settings.StreamBufferCharacters];
        var readyObserved = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (count == 0)
            {
                if (ExceededBound)
                {
                    throw new InvalidOperationException(FailureMessage);
                }
                return CapturedOutput;
            }
            if (output.Length + count > settings.MaximumOutputCharacters)
            {
                ExceededBound = true;
            }
            else if (!ExceededBound)
            {
                output.Append(buffer, 0, count);
            }
            if (ready is not null && !readyObserved
                && CapturedOutput.Contains(OpenLoopPlanNodeProgram.ReadyMarker, StringComparison.Ordinal))
            {
                readyObserved = true;
                ready.TrySetResult();
            }
        }
    }
}
