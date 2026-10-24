using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

/// <summary>Retains actual reader completion and retirement failure under the owning generation gate.</summary>
internal sealed class AnnGenerationReadLifetime(Lock gate)
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

    internal AnnIndexLease Acquire(AnnGenerationSlot slot)
    {
        lock (gate)
        {
            var pendingZero = readers == Empty ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : noReaders;
            var lease = slot.Acquire();
            noReaders = pendingZero;
            readers++;
            return lease;
        }
    }

    internal void Release(AnnGenerationSlot slot, AnnRootLease root,
        List<AnnGenerationSlot> slots, IOptions<AnnExecutionOptions> configured)
    {
        lock (gate)
        {
            slot.ReleaseUnderOwnerGate();
            readers--;
            try
            {
                AnnSlotRetention.RetireUnpinned(root, slots, configured.Value);
            }
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
        {
            noReaders?.TrySetResult();
        }
        else
        {
            noReaders?.TrySetException(readerFailure);
        }
        noReaders = null;
    }
}
