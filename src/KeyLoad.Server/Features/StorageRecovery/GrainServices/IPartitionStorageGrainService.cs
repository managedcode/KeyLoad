using Orleans.CodeGeneration;
using Orleans.Services;

namespace KeyLoad.Server;

/// <summary>Native silo-local lifecycle identity for the original physical storage owner.</summary>
[Alias(IPartitionStorageGrainService.ServiceAlias), Version(IPartitionStorageGrainService.InterfaceVersion)]
internal interface IPartitionStorageGrainService : IGrainService
{
    internal const string ServiceAlias = "keyload.storage.partition.grain-service.v1";
    internal const int InterfaceVersion = 1;
}
