using KeyLoad.Core;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace KeyLoad.Clustering;

/// <summary>Owns a single partition's queue; interleaving admits batches while its one drain awaits copies.</summary>
[Reentrant]
public sealed class PartitionCommitGrain(SiloDatabaseClient storage, IClusterMembershipService membership,
    IOptions<ReplicaCopyOptions> options, ILogger<PartitionCommitGrain> diagnostics)
    : Grain, IPartitionCommitGrain
{
    private StorageCommitQueue? commands;
    private SiloStorageCopyExecution? copies;
    private IGrainTimer? synchronization;

    /// <inheritdoc />
    public async Task<Result<TResult>> ApplyAsync<TResult>(Guid incarnation, PartitionRef partition,
        Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        Bind(incarnation, partition);
        if (StorageCommandKeys.PartitionOf(operation) != partition)
        {
            throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }
        try
        {
            return await commands!.Submit<TResult>(operation, cancellationToken);
        }
        finally
        {
            EnsureSynchronization();
        }
    }

    /// <inheritdoc />
    public async Task<SiloAddress> GetReadOwnerAsync(Guid incarnation, PartitionRef partition, SiloAddress preferred,
        CancellationToken cancellationToken)
    {
        Bind(incarnation, partition);
        try
        {
            return await copies!.GetReadOwnerAsync(preferred, cancellationToken);
        }
        finally
        {
            EnsureSynchronization();
        }
    }

    private void Bind(Guid incarnation, PartitionRef partition)
    {
        if (PartitionCommitGrainIdentity.For(incarnation, partition) != this.GetPrimaryKeyString())
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        if (copies is not null)
        {
            return;
        }
        options.Value.Validate();
        copies = new(storage, membership, options, incarnation, diagnostics, partition);
        commands = new(copies);
    }

    /// <inheritdoc />
    public Task WaitForCopiedAsync(Guid incarnation, PartitionRef partition, long position, CancellationToken cancellationToken)
    {
        Bind(incarnation, partition);
        var waiting = copies!.WaitForCopiedAsync(position, cancellationToken);
        EnsureSynchronization();
        return waiting;
    }

    private void EnsureSynchronization()
    {
        if (synchronization is null && copies is { NeedsSynchronization: true }
            && options.Value is { StorageMode: StorageAccessMode.IsolatedVolumes, ReplicaCount: > 1 })
        {
            synchronization = this.RegisterGrainTimer(SynchronizeAsync, new GrainTimerCreationOptions
            {
                DueTime = options.Value.SynchronizationInterval,
                Period = options.Value.SynchronizationInterval,
                Interleave = true,
                KeepAlive = true
            });
        }
    }

    private async Task SynchronizeAsync(CancellationToken token)
    {
        await commands!.SynchronizeAsync(token);
        if (copies is { NeedsSynchronization: false })
        {
            synchronization?.Dispose();
            synchronization = null;
        }
    }

    /// <inheritdoc />
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        synchronization?.Dispose();
        copies?.StopWaiting();
        if (commands is { } original)
        {
            await original.JoinAsync();
        }
        await base.OnDeactivateAsync(reason, cancellationToken);
    }
}
