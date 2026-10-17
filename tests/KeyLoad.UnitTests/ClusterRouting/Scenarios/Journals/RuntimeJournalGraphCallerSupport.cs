using System.Runtime.ExceptionServices;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RuntimeJournalGraphCallerSupport
{
    private const string GraphTransitionDenialPrefix = "Transition from ";
    private const string GraphTransitionDenialSeparator = " to ";
    private const string GraphTransitionDenialSuffix = " is not allowed.";

    internal static void RequireGraphDenial(InvalidOperationException failure, string source, string target)
    {
        var expected = string.Concat(GraphTransitionDenialPrefix, source, GraphTransitionDenialSeparator,
            target, GraphTransitionDenialSuffix);
        if (!string.Equals(failure.Message, expected, StringComparison.Ordinal))
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal static async Task WaitForActivationChangeAsync(IRuntimeJournalReplayGrain grain,
        string previousToken, NativeRuntimeTestOptions timing)
    {
        using var deadline = new CancellationTokenSource(timing.CompletionTimeout, TimeProvider.System);
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (await grain.GetActivationTokenAsync().WaitAsync(deadline.Token).ConfigureAwait(false) != previousToken)
            {
                return;
            }

            await Task.Delay(timing.PollInterval, TimeProvider.System, deadline.Token).ConfigureAwait(false);
        }
    }
}
