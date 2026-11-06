using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

/// <summary>Orders one atomic partition independently of every other partition's copy waits.</summary>
public interface IPartitionCommitGrain : IGrainWithStringKey
{
    /// <summary>Returns after every configured copy has persisted the primary's exact effects.</summary>
    Task<Result<TResult>> ApplyAsync<TResult>(Guid incarnation, PartitionRef partition,
        Command<DatabaseCommand> operation, CancellationToken cancellationToken);

    /// <summary>Selects an available copy of this partition's current committed cut.</summary>
    Task<SiloAddress> GetReadOwnerAsync(Guid incarnation, PartitionRef partition, SiloAddress preferred,
        CancellationToken cancellationToken);

    /// <summary>Waits for a specific persisted prefix without joining a later dependent command.</summary>
    Task WaitForCopiedAsync(Guid incarnation, PartitionRef partition, long position, CancellationToken cancellationToken);
}
