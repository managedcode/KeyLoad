using System.Runtime.ExceptionServices;
using global::Orleans.Streams;

namespace KeyLoad.Clustering;

internal static class EventFeedAdvisoryPublisher
{

    internal static async Task PublishAsync(IAsyncObserver<EventFeedWakeupHint> actualProducer,
        EventFeedWakeupHint actualHint, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actualProducer) /* KLD0041: BCL null validation accepts the actualProducer IAsyncObserver reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(actualHint) /* KLD0041: BCL null validation accepts the actualHint EventFeedWakeupHint reference through its object parameter. */;
        originalCancellationToken.ThrowIfCancellationRequested();
        var /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */ original = (CaptureContext() /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */);
        Exception? nonfatal = null;
        Exception? fatal = null;
        Exception? settlement = null;
        try
        {
            RequestContext.Clear();
            // The execution enqueue Task is not cancellation-aware. Join its ORIGINAL settlement before restoration.
            await actualProducer.OnNextAsync(actualHint, null).ConfigureAwait(true);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            nonfatal = error;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            fatal = error;
        }
        finally
        {
            settlement = await CqrsStreamSettlement.SettleAsync<EventFeedWakeupHint>(null,
                () =>
                {
                    RestoreContext(original) /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */;
                }, nonfatal ?? fatal).ConfigureAwait(true);
        }
        if ((settlement ?? nonfatal ?? fatal) is { } terminal)
        {
            ExceptionDispatchInfo.Capture(terminal).Throw();
        }
        originalCancellationToken.ThrowIfCancellationRequested();
    }

    private static List<KeyValuePair<string, object>> /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */ CaptureContext()
    {
        var /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */ original = (new List<KeyValuePair<string, object>>() /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */);
        foreach (var entry in (RequestContext.Entries /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */))
        {
            // Borrow the already-owned caller context; never encode or enqueue its identity/data.
            original.Add((new(entry.Key, (entry.Value /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */)) /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */)) /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */;
        }
        return original;
    }

    private static void RestoreContext(List<KeyValuePair<string, object>> /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */ original)
    {
        RequestContext.Clear();
        foreach (var entry in original)
        {
            RequestContext.Set(entry.Key, (entry.Value /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */)) /* KLD0041: Borrow and restore the original heterogeneous Orleans RequestContext entries after the actual advisory enqueue settles. */;
        }
    }
}
