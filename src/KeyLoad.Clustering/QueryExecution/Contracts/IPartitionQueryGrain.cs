namespace KeyLoad.Clustering;

/// <summary>Addresses bounded reads for one canonical logical partition through native Orleans placement.</summary>
[Orleans.CodeGeneration.Version(PartitionQueryGrainIdentity.InterfaceVersion)]
internal interface IPartitionQueryGrain : IGrainWithStringKey
{
    Task<(SiloAddress Address, PhysicalShardRecord Owner)> ResolveOwnerAsync(Guid incarnation,
        CancellationToken cancellationToken);

    Task<GrainOperationReply> ExecuteAsync(SiloAddress owner, string signedRequest,
        CancellationToken cancellationToken);
}
