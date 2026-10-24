namespace KeyLoad.Core.BlobStorage;

[Orleans.GenerateSerializer, Orleans.Alias(FileStorageProtocol.AddressAlias)]
internal sealed record FileAddress([property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Resource, [property: Orleans.Id(2)] string Path);
