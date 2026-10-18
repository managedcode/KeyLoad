using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Orleans owns this atomic partition's ordering; storage and heavy execution stay in silo-local services and workers.</summary>
public sealed class CommandPartitionGrain(SiloDatabaseClient storage, DatabaseEngine database)
    : Grain, ICommandPartitionGrain
{
    /// <inheritdoc />
    public Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken)
        => storage.ExecuteCommandAsync(database.Store.Identity.Incarnation, signedRequest,
            this.GetPrimaryKeyString(), cancellationToken);
}
