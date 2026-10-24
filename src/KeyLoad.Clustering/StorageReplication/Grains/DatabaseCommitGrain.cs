using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace KeyLoad.Clustering;

/// <summary>Orders canonical commands into batches and owns the shared-file activation when configured.</summary>
/// <remarks>Calls interleave: each command joins the queue and one drain applies every queued command as one commit on
/// every copy, so commands that arrive while a batch persists share the next batch's single journal flush.</remarks>
[Reentrant]
public sealed partial class DatabaseCommitGrain(SiloDatabaseClient storage, IClusterMembershipService membership,
    IOptions<ReplicaCopyOptions> options, ILogger<DatabaseCommitGrain> diagnostics, ILocalDatabaseRuntime? localRuntime = null)
    : Grain, IDatabaseCommitGrain
{
    private readonly Queue<StorageCommitRequest> pending = new();
    private Task drain = Task.CompletedTask;
    private SiloStorageCopyExecution? copies;
    private SiloStorageCopyExecution Copies => copies ??= new(storage, membership, options, this.GetPrimaryKey(), diagnostics);
    private bool UsesSharedVolume => options.Value.StorageMode == StorageAccessMode.SharedVolume;

    /// <inheritdoc />
    public Task<Result<object>> ApplyAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        if (UsesSharedVolume)
        {
            return Shared.ApplyAsync(operation, cancellationToken);
        }

        var request = new StorageCommitRequest(operation, cancellationToken);
        pending.Enqueue(request);
        if (drain.IsCompleted)
        {
            drain = DrainAsync();
        }
        return request.Completion;
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
                outcomes = await Copies.ApplyBatchAsync([.. batch.Select(request => request.Operation)],
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

    /// <inheritdoc />
    public Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken)
        => UsesSharedVolume ? Task.FromResult(Shared.ReadCut(cancellationToken)) : Copies.ReadCutAsync(cancellationToken);

    /// <inheritdoc />
    public Task<SiloAddress> GetReadOwnerAsync(SiloAddress preferredSilo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UsesSharedVolume ? Task.FromResult(Shared.Address)
            : Copies.GetReadOwnerAsync(preferredSilo, cancellationToken);
    }
}
