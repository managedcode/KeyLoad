using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Orleans serializes canonical commands and owns the shared-file activation when configured.</summary>
public sealed partial class DatabaseCommitGrain(SiloDatabaseClient storage, IClusterMembershipService membership,
    IOptions<ReplicaCopyOptions> options, ILogger<DatabaseCommitGrain> diagnostics, ILocalDatabaseRuntime? localRuntime = null)
    : Grain, IDatabaseCommitGrain
{
    private SiloStorageCopyExecution? copies;
    private SiloStorageCopyExecution Copies => copies ??= new(storage, membership, options, this.GetPrimaryKey(), diagnostics);
    private bool UsesSharedVolume => options.Value.StorageMode == StorageAccessMode.SharedVolume;

    /// <inheritdoc />
    public Task<Result<object>> ApplyAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
        => UsesSharedVolume ? Shared.ApplyAsync(operation, cancellationToken) : Copies.ApplyAsync(operation, cancellationToken);

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
