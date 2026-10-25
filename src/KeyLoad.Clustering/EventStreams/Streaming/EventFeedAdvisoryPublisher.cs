using System.Runtime.ExceptionServices;
using global::Orleans.Streams;

namespace KeyLoad.Clustering;

internal static class EventFeedAdvisoryPublisher
{

    internal static async Task PublishAsync(IAsyncObserver<EventFeedWakeupHint> actualProducer,
        EventFeedWakeupHint actualHint, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actualProducer);
        ArgumentNullException.ThrowIfNull(actualHint);
        originalCancellationToken.ThrowIfCancellationRequested();
        var original = CaptureContext();
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
                () => RestoreContext(original), nonfatal ?? fatal).ConfigureAwait(true);
        }
        if ((settlement ?? nonfatal ?? fatal) is { } terminal)
        {
            ExceptionDispatchInfo.Capture(terminal).Throw();
        }
        originalCancellationToken.ThrowIfCancellationRequested();
    }

    private static List<KeyValuePair<string, object>> CaptureContext()
    {
        var original = new List<KeyValuePair<string, object>>();
        foreach (var entry in RequestContext.Entries)
        {
            // Borrow the already-owned caller context; never encode or enqueue its identity/data.
            original.Add(new(entry.Key, entry.Value));
        }
        return original;
    }

    private static void RestoreContext(List<KeyValuePair<string, object>> original)
    {
        RequestContext.Clear();
        foreach (var entry in original)
        {
            RequestContext.Set(entry.Key, entry.Value);
        }
    }
}
