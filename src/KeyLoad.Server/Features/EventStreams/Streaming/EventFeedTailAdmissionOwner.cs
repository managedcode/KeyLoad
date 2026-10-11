using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Server.Features.EventStreams;

/// <summary>Owns the original enumerator and joins cancellation/disposal while preserving every initiating failure.</summary>
internal sealed class EventFeedTailAdmissionOwner : IAsyncDisposable
{
    private const int NoFailures = 0;
    private const int SingleRefusal = 1;
    private readonly CancellationTokenSource cancellation;
    private readonly EventFeedTailJsonGrant grant;
    private readonly List<Exception> failures = [];
    private readonly IAsyncEnumerator<CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>>? reader;
    internal KeyLoadException? Quota { get; private set; }
    internal CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult> Current => reader!.Current;

    internal EventFeedTailAdmissionOwner(
        IAsyncEnumerable<CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>> source,
        EventFeedTailJsonGrant grant, CancellationToken cancellationToken)
    {
        this.grant = grant;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IAsyncEnumerator<CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>>? original = null;
        ServerFailureObserver.Observe(() => original = source.GetAsyncEnumerator(cancellation.Token), failures);
        reader = original;
    }

    internal async ValueTask<bool> MoveNextAsync()
    {
        if (failures.Count != NoFailures || reader is null)
        { return false; }
        var available = false;
        await ServerFailureObserver.ObserveAsync(ReadOriginalAsync, failures).ConfigureAwait(false);
        if (!available || failures.Count != NoFailures)
        { return false; }
        ServerFailureObserver.Observe(() => grant.Admit(reader.Current), failures);
        if (failures.Count != NoFailures)
        {
            Quota = failures[NoFailures] as KeyLoadException;
            if (Quota?.Code != ErrorCode.ResourceExhausted)
            { Quota = null; }
            return false;
        }
        return true;

        async Task ReadOriginalAsync()
        { available = await reader.MoveNextAsync().ConfigureAwait(false); }
    }

    public async ValueTask DisposeAsync()
    {
        await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures).ConfigureAwait(false);
        if (reader is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => reader.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        try
        { cancellation.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
            throw new AggregateException(failures);
        }
        if (Quota is null || failures.Count != SingleRefusal)
        { ServerFailureObserver.ThrowIfAny(failures); }
    }
}
