namespace KeyLoad.Clustering;

/// <summary>Orleans owns this atomic partition's ordering; storage and heavy execution stay in silo-local services and workers.</summary>
public sealed class CommandPartitionGrain(SiloDatabaseClient storage, GrainRequestCodec codec)
    : Grain, ICommandPartitionGrain
{
    /// <inheritdoc />
    public Task<GrainOperationReply> ExecuteAsync(GrainRequestEnvelope envelope, CancellationToken cancellationToken)
        => storage.ExecuteCommandAsync(codec.Incarnation, envelope,
            this.GetPrimaryKeyString(), cancellationToken);
}
