using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal readonly record struct RequestCqrsProbePartitionRecord(
    [property: JsonRequired] string TenantId, [property: JsonRequired] string DatabaseId,
    [property: JsonRequired] string TransactionDomainId, [property: JsonRequired] string PartitionKey)
{
    internal PartitionRef ToPartition() => new(TenantId, DatabaseId, TransactionDomainId, PartitionKey);
}
