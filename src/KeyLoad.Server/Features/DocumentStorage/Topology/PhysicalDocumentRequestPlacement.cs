using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class PhysicalDocumentRequestPlacement(ILocalSiloDetails local, PhysicalShardRecord owner) : IPhysicalRequestPlacement
{
    public SiloAddress Current => local.SiloAddress;
    public PhysicalShardRecord Owner => owner;
}
