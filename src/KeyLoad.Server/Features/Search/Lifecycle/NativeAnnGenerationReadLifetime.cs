using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

/// <summary>Retains actual reader completion and retirement failure under the owning generation gate.</summary>
internal sealed class NativeAnnGenerationReadLifetime(Lock gate, int maximumReaders)
{
    private const int Empty = 0;
    private TaskCompletionSource? noReaders;
    private int readers;
    private Exception? readerFailure;

    internal Task OriginalReaders
    {
        get
        {
            lock (gate)
            {
                return noReaders?.Task ?? (readerFailure is null ? Task.CompletedTask : Task.FromException(readerFailure));
            }
        }
    }

    internal NativeAnnIndexLease Acquire(NativeAnnGenerationSlot slot)
    {
        lock (gate)
        {
            if (readers == maximumReaders)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, NativeAnnProtocol.Bound); }
            var pendingZero = readers == Empty ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : noReaders;
            var lease = slot.Acquire(maximumReaders);
            noReaders = pendingZero;
            readers++;
            return lease;
        }
    }

    internal void Release(NativeAnnGenerationSlot slot, NativeAnnRootLease root,
        List<NativeAnnGenerationSlot> slots, IOptions<NativeAnnExecutionOptions> configured)
    {
        lock (gate)
        {
            slot.ReleaseUnderOwnerGate();
            readers--;
            try
            { NativeAnnSlotRetention.RetireUnpinned(root, slots, configured.Value); }
            catch (Exception error)
            {
                readerFailure = readerFailure is null ? error : new AggregateException(readerFailure, error);
                throw;
            }
            finally { if (readers == Empty) { SignalReaders(); } }
        }
    }

    private void SignalReaders()
    {
        if (readerFailure is null)
        { noReaders?.TrySetResult(); }
        else
        { noReaders?.TrySetException(readerFailure); }
        noReaders = null;
    }
}
