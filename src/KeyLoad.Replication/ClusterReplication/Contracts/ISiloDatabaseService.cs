using Orleans.Services;

namespace KeyLoad.Replication;

/// <summary>Typed Orleans access to one silo's exclusively owned ZoneTree database.</summary>
[Alias(StorageCopyContract.ServiceAlias)]
public interface ISiloDatabaseService : IGrainService
{
    /// <summary>Reads the durable local cut without changing storage.</summary>
    [Alias(StorageCopyContract.Cut)]
    Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken);
    /// <summary>Evaluates business time and deterministic command decisions at the selected store.</summary>
    [Alias(StorageCopyContract.Prepare)]
    Task<ReplicatedOperation> PrepareAsync(ReplicatedOperation operation, CancellationToken cancellationToken);
    /// <summary>Applies exactly the prepared operation through ZoneTree's atomic commit gate.</summary>
    [Alias(StorageCopyContract.Apply)]
    Task<OperationResult> ApplyAsync(ReplicatedOperation operation, CancellationToken cancellationToken);
}
