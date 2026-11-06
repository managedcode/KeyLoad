using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

/// <summary>Execution Orleans scheduling for commands sharing one database's atomic metadata.</summary>
public partial interface IDatabaseCommitGrain : IGrainWithGuidKey
{
    /// <summary>Returns only after every configured data copy has persisted the same operation.</summary>
    Task<Result<TResult>> ApplyAsync<TResult>(Command<DatabaseCommand> operation, CancellationToken cancellationToken);
    /// <summary>Returns the committed canonical cut from an available execution storage service.</summary>
    Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken);
    /// <summary>Returns an actual execution address only after verifying the durable data-copy cut.</summary>
    Task<SiloAddress> GetReadOwnerAsync(SiloAddress preferredSilo, CancellationToken cancellationToken);
    /// <summary>Selects a copy of the current persisted metadata for partition authorization.</summary>
    Task<SiloAddress> GetMetadataOwnerAsync(SiloAddress preferredSilo, CancellationToken cancellationToken);
    /// <summary>Selects the common current primary whose native transactions order catalog and data decisions.</summary>
    Task<SiloAddress> GetWriteOwnerAsync(CancellationToken cancellationToken);
    /// <summary>Waits for the specified metadata prefix on every selected copy.</summary>
    Task WaitForCopiedAsync(long position, CancellationToken cancellationToken);
}
