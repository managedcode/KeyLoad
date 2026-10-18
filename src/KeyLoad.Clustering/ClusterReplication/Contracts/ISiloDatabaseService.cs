using Orleans.Services;

using Orleans.CodeGeneration;

namespace KeyLoad.Clustering;

/// <summary>Typed Orleans access to one silo's exclusively owned ZoneTree database.</summary>
[Version(BackgroundWorkerIdentity.InterfaceVersion)]
public interface ISiloDatabaseService : IGrainService
{
    /// <summary>Reads the durable local cut without changing storage.</summary>
    Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken);
    /// <summary>Evaluates business time and deterministic command decisions at the selected store.</summary>
    Task<ReplicatedOperation> PrepareAsync(ReplicatedOperation operation, CancellationToken cancellationToken);
    /// <summary>Applies exactly the prepared operation through ZoneTree's atomic commit gate.</summary>
    Task<OperationResult> ApplyAsync(ReplicatedOperation operation, CancellationToken cancellationToken);
}
