using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;
using global::Orleans.Streams;

namespace KeyLoad.Orleans;

internal sealed class EventFeedAdvisoryPublisher(IOptions<DatabaseLimits> databaseOptions)
{
    private const string InvalidContext = "The native event feed advisory context exceeds its bound.";

    internal async Task PublishAsync(IAsyncObserver<EventFeedWakeupHint> actualProducer,
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
            // The native enqueue Task is not cancellation-aware. Join its ORIGINAL settlement before restoration.
            await actualProducer.OnNextAsync(actualHint, null).ConfigureAwait(true);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { nonfatal = error; }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { fatal = error; }
        finally
        {
            settlement = await NativeCqrsStreamSettlement.SettleAsync<EventFeedWakeupHint>(null,
                () => RestoreContext(original), nonfatal ?? fatal).ConfigureAwait(true);
        }
        if ((settlement ?? nonfatal ?? fatal) is { } terminal)
        { ExceptionDispatchInfo.Capture(terminal).Throw(); }
        originalCancellationToken.ThrowIfCancellationRequested();
    }

    private List<KeyValuePair<string, object>> CaptureContext()
    {
        var limits = databaseOptions.Value;
        limits.Validate();
        var original = new List<KeyValuePair<string, object>>();
        foreach (var entry in RequestContext.Entries)
        {
            if (original.Count >= limits.MaxResults)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidContext); }
            // Borrow the already-owned caller context; never encode or enqueue its identity/data.
            original.Add(new(entry.Key, entry.Value));
        }
        return original;
    }

    private static void RestoreContext(List<KeyValuePair<string, object>> original)
    {
        RequestContext.Clear();
        foreach (var entry in original) { RequestContext.Set(entry.Key, entry.Value); }
    }
}
