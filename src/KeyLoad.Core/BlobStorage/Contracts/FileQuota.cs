namespace KeyLoad.Core.Features.BlobStorage;

[Orleans.GenerateSerializer, Orleans.Alias(FileQuota.ContractAlias)]
internal sealed record FileQuota([property: Orleans.Id(0)] long AllocatedBytes = FileStorageProtocol.EmptyCount,
    [property: Orleans.Id(1)] int ActiveUploads = FileStorageProtocol.EmptyCount, [property: Orleans.Id(2)] int Objects = FileStorageProtocol.EmptyCount,
    [property: Orleans.Id(3)] int Files = FileStorageProtocol.EmptyCount)
{
    internal const string ContractAlias = "keyload.file.quota.v1";
}
