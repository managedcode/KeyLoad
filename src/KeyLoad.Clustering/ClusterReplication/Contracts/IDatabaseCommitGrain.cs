using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using Orleans.CodeGeneration;

namespace KeyLoad.Clustering;

/// <summary>Native Orleans scheduling for commands sharing one database's atomic metadata.</summary>
[Version(BackgroundWorkerIdentity.InterfaceVersion)]
public interface IDatabaseCommitGrain : IGrainWithGuidKey
{
    /// <summary>Returns only after every configured data copy has persisted the same operation.</summary>
    Task<Result<object>> ApplyAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken);
    /// <summary>Returns the committed canonical cut from an available native storage service.</summary>
    Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken);
    /// <summary>Returns an actual native address only after verifying the durable data-copy cut.</summary>
    Task<SiloAddress> GetReadOwnerAsync(SiloAddress preferredSilo, CancellationToken cancellationToken);
}
