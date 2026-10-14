using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedTailRf3PairJoin
{
    private const string Missing = "The original two-offer Tail operation did not return its actual terminal result.";
    internal static async Task<(EventFeedControlRequest Request, EventFeedControlResult Result)> RunAsync<T>(
        Task<T> originalOperation, Func<T, ValueTask<EventFeedControlResult>> decode, EventFeedTailRf3Pair progress,
        EventFeedControlResult first, Func<Task<(EventFeedControlRequest Request, EventFeedControlResult Result)>> next,
        CancellationTokenSource owned, CancellationToken token)
    {
        var failures = new List<Exception>();
        (EventFeedControlRequest Request, EventFeedControlResult Result)? expected = null;
        EventFeedControlResult? terminal = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await progress.WaitFirstAsync(originalOperation, token).ConfigureAwait(false);
                await progress.RequireFirstAsync(first).ConfigureAwait(false);
                expected = await next().ConfigureAwait(false);
                await ((Task)originalOperation).WaitAsync(token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                token.ThrowIfCancellationRequested();
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            await ServerFailureObserver.ObserveAsync(owned.CancelAsync, failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                terminal = await decode(await originalOperation.ConfigureAwait(false)).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        var pair = expected ?? throw new InvalidOperationException(Missing);
        await SqlRf3Protocol.EqualAsync(pair.Result, terminal ?? throw new InvalidOperationException(Missing));
        await progress.RequireBothAsync(first, pair.Result).ConfigureAwait(false);
        return pair;
    }
}
