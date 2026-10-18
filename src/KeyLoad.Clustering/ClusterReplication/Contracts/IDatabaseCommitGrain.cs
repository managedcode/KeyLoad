using Orleans.CodeGeneration;

namespace KeyLoad.Clustering;

/// <summary>Native Orleans scheduling for commands sharing one database's atomic metadata.</summary>
[Version(BackgroundWorkerIdentity.InterfaceVersion)]
public interface IDatabaseCommitGrain : IGrainWithGuidKey
{
    /// <summary>Returns only after every configured data copy has persisted the same operation.</summary>
    Task<OperationResult> ApplyAsync(ReplicatedOperation operation, CancellationToken cancellationToken);
    /// <summary>Requires matching durable copies before admitting a read.</summary>
    Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken);
    /// <summary>Returns an actual native address only after verifying the durable data-copy cut.</summary>
    Task<SiloAddress> GetReadOwnerAsync(CancellationToken cancellationToken);
}
