using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedTailRf3CancellationJoin
{
    private const string MissingProgress = "The original Tail operation settled before its required first notification.";
    internal static async Task JoinAsync(Task<CallToolResult> originalOperation, EventFeedTailRf3Progress progress,
        EventFeedControlResult original, CancellationTokenSource owned, int expectedTotal, CancellationToken token)
    {
        var failures = new List<Exception>();
        var cancelled = false;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var firstProgress = progress.WaitAsync(token);
                var first = await Task.WhenAny(firstProgress, originalOperation).ConfigureAwait(false);
                if (ReferenceEquals(first, originalOperation))
                {
                    _ = await McpCallerAssertions.SuccessAsync<EventFeedControlResult>(
                        await originalOperation.ConfigureAwait(false));
                    throw new InvalidOperationException(MissingProgress);
                }
                await firstProgress.ConfigureAwait(false);
                await progress.RequireAsync(original, expectedTotal).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            await ServerFailureObserver.ObserveAsync(owned.CancelAsync, failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                try
                {
                    _ = await originalOperation.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (owned.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    cancelled = true;
                }
            }, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(cancelled).IsTrue();
    }
}
