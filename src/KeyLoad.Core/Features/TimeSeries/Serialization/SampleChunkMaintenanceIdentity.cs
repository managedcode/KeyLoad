using System.Security.Cryptography;

namespace KeyLoad.Core.Features.TimeSeries;

[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkMaintenanceIdentity.SerializerAlias)]
internal sealed record SampleChunkMaintenanceIdentity([property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string Series,
    [property: Orleans.Id(3)] Guid WindowId, [property: Orleans.Id(4)] long Generation,
    [property: Orleans.Id(5)] long Revision)
{
    internal const string SerializerAlias = "keyload.core.sample-chunk-maintenance-identity.v1";
    private const int GuidBytes = 16;

    internal Guid CommandId()
    {
        var digest = SHA256.HashData(KeyLoad.Storage.KeyCodec.Encode(SerializerAlias, Partition.TenantId,
            Partition.DatabaseId, Partition.TransactionDomainId, Partition.PartitionKey, Set, Series,
            WindowId, Generation, Revision));
        return new Guid(digest.AsSpan(SampleChunkLifecycleProtocol.FirstIndex, GuidBytes));
    }
}
