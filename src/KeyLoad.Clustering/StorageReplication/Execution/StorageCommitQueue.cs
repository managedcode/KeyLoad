using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Joins canonical batches and background copy completion on the same ordinary owner context.</summary>
internal sealed class StorageCommitQueue(SiloStorageCopyExecution copies)
{
    private readonly Queue<StorageCommitRequest> pending = new();
    private Task drain = Task.CompletedTask;

    internal Task<Result<object>> Submit(Command<DatabaseCommand> operation, CancellationToken token)
    {
        var request = new StorageCommitRequest(operation, token);
        pending.Enqueue(request);
        if (drain.IsCompleted)
        {
            drain = DrainAsync();
        }
        return request.Completion;
    }

    internal Task SynchronizeAsync(CancellationToken token)
    {
        if (drain.IsCompleted)
        {
            drain = SynchronizeAndDrainAsync(token);
            return drain;
        }
        return Task.CompletedTask;
    }

    internal Task JoinAsync() => drain;

    private async Task SynchronizeAndDrainAsync(CancellationToken token)
    {
        var synchronization = (await RequestWorkSettlement.JoinAsync(copies.SynchronizeAsync(token)).ConfigureAwait(true)).Value;
        var queued = (await RequestWorkSettlement.JoinAsync(DrainAsync()).ConfigureAwait(true)).Value;
        RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(synchronization, queued));
    }

    /// <summary>Applies queued commands in submission order until the queue is empty.</summary>
    private async Task DrainAsync()
    {
        while (pending.Count > 0)
        {
            var batch = new List<StorageCommitRequest>(pending.Count);
            while (pending.TryDequeue(out var request))
            {
                if (request.TrySend())
                {
                    batch.Add(request);
                }
            }
            if (batch.Count == 0)
            {
                continue;
            }

            StorageCopyOutcome[] outcomes;
            try
            {
                outcomes = await copies.ApplyBatchAsync([.. batch.Select(request => request.Operation)],
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
            {
                outcomes = [.. batch.Select(_ => StorageCopyOutcome.Failed(error))];
            }
            Settle(batch, outcomes);
        }
    }

    /// <summary>Completes decided commands and returns the rest, in order, ahead of commands queued meanwhile.</summary>
    private void Settle(List<StorageCommitRequest> batch, StorageCopyOutcome[] outcomes)
    {
        var waiting = pending.ToArray();
        pending.Clear();
        for (var index = 0; index < batch.Count; index++)
        {
            if (outcomes[index].Decided)
            {
                batch[index].Complete(outcomes[index]);
            }
            else if (batch[index].TryReturn())
            {
                pending.Enqueue(batch[index]);
            }
            else
            {
                batch[index].Release();
            }
        }
        foreach (var request in waiting)
        {
            pending.Enqueue(request);
        }
    }
}
