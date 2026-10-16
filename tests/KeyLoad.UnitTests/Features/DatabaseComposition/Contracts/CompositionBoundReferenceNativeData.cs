using System.Text.Json;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionBoundReferenceNativeData
{
    internal const string Root = "root";
    internal const string From = "composition-bound-from";
    internal const string To = "composition-bound-to";
    internal const string Graph = "composition-bound-graph";
    internal const string RoundGraph = "composition-bound-round-graph";
    internal const string EmptyGraph = "composition-bound-empty-graph";
    internal const string Queue = "composition-bound-queue";
    internal const string ReverseQueue = "composition-bound-reverse";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Message = "original";
    internal const string ForwardPrefix = "forward-";
    internal const string ReversePrefix = "reverse-";
    internal const string RoundPrefix = "round-";
    internal const string Label = "related";
    internal const string Attributes = "{\"weight\":7}";
    internal const string Headers = "{}";
    internal const string FirstJson = "{\"id\":\"first\",\"name\":\"first\"}";
    internal const string SecondJson = "{\"id\":\"second\",\"name\":\"second\"}";
    internal const string EdgeId = ForwardPrefix + Message;
    internal const string DerivedId = ReversePrefix + EdgeId;
    internal const string RoundEdgeId = RoundPrefix + DerivedId;

    // Independent fixed-shape golden property order; production serializers/canonicalizers are not invoked.
    internal static string Payload(EntityRef from, EntityRef to) => JsonSerializer.Serialize(new
    {
        attributesJson = Attributes,
        from = Reference(from),
        label = Label,
        to = Reference(to)
    }, JsonDefaults.Options);

    internal static string OpaquePayload(EntityRef from, EntityRef to)
        => "  { \"to\": " + JsonSerializer.Serialize(to, JsonDefaults.Options)
            + ", \"label\": \"related\", \"attributesJson\": " + JsonSerializer.Serialize(Attributes, JsonDefaults.Options)
            + ", \"from\": " + JsonSerializer.Serialize(from, JsonDefaults.Options) + " }  ";

    private static object Reference(EntityRef reference)
    {
        var partition = new
        {
            atomicPartitionId = reference.Partition.AtomicPartitionId,
            databaseId = reference.Partition.DatabaseId,
            partitionKey = reference.Partition.PartitionKey,
            tenantId = reference.Partition.TenantId,
            transactionDomainId = reference.Partition.TransactionDomainId
        };
        return reference.Lifetime is { } lifetime
            ? new
            {
                collection = reference.Collection,
                id = reference.Id,
                lifetime = new
                {
                    incarnation = lifetime.Incarnation
                },
                partition
            }
            : (object)new
            {
                collection = reference.Collection,
                id = reference.Id,
                partition
            };
    }
}
