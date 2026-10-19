namespace KeyLoad.Clustering;

/// <summary>Addresses bounded reads for one canonical logical partition through execution Orleans placement.</summary>
[Orleans.CodeGeneration.Version(PartitionQueryGrainIdentity.InterfaceVersion)]
internal interface IPartitionQueryGrain : IGrainWithStringKey
{
    Task<(SiloAddress Address, PhysicalShardRecord Owner)> ResolveOwnerAsync(Guid incarnation,
        CancellationToken cancellationToken);

    Task<GrainOperationReply> ExecuteAsync(SiloAddress owner, string signedRequest,
        CancellationToken cancellationToken);
}
