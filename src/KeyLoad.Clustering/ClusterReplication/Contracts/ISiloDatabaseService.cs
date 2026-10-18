using Orleans.Services;
using ManagedCode.Communication.CQRS;

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
    Task<StorageOperationResult> ApplyAsync(ReplicatedOperation operation, long sequence, CancellationToken cancellationToken);
    /// <summary>Runs native CQRS execution on this store's local stateless-worker pool.</summary>
    IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        string signedRequest, CancellationToken cancellationToken);
    /// <summary>Executes the original partition-ordered command on this silo's command-worker pool.</summary>
    Task<GrainOperationReply> ExecuteCommandAsync(string signedRequest, string actorKey,
        CancellationToken cancellationToken);
}
