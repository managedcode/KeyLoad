using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
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
    private SiloStorageCopyExecution? copies;
    private StorageCommitQueue? commands;
    private IGrainTimer? synchronizationTimer;
    private SiloStorageCopyExecution Copies => copies ??= new(storage, membership, options, this.GetPrimaryKey(), diagnostics);
    private StorageCommitQueue Commands => commands ??= new(Copies);
    private bool UsesSharedVolume => options.Value.StorageMode == StorageAccessMode.SharedVolume;

    /// <inheritdoc />
    public Task<Result<object>> ApplyAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
        => UsesSharedVolume ? Shared.ApplyAsync(operation, cancellationToken) : Commands.Submit(operation, cancellationToken);

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
