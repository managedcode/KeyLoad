namespace KeyLoad.Replication;

/// <summary>Native Orleans scheduling for commands sharing one database's atomic metadata.</summary>
[Alias(StorageCopyContract.GrainAlias)]
public interface IDatabaseCommitGrain : IGrainWithGuidKey
{
    /// <summary>Returns only after every configured data copy has persisted the same operation.</summary>
    [Alias(StorageCopyContract.Apply)]
    Task<OperationResult> ApplyAsync(ReplicatedOperation operation, CancellationToken cancellationToken);
    /// <summary>Requires matching durable copies before admitting a read.</summary>
    [Alias(StorageCopyContract.ReadCut)]
    Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken);
}
