namespace KeyLoad.Clustering;

/// <summary>Addresses bounded reads for one canonical logical partition through execution Orleans placement.</summary>
internal interface IPartitionQueryGrain : IGrainWithStringKey
{
    Task<(SiloAddress Address, PhysicalShardRecord Owner)> ResolveOwnerAsync(Guid incarnation,
        CancellationToken cancellationToken);

    Task<GrainOperationReply> ExecuteAsync(SiloAddress owner, GrainRequestEnvelope envelope,
        CancellationToken cancellationToken);
}
