namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorAuthorizationScope.SerializerAlias)]
internal sealed record EventVectorAuthorizationScope(
    [property: Orleans.Id(EventVectorAuthorizationScope.PartitionField)] PartitionRef Partition,
    [property: Orleans.Id(EventVectorAuthorizationScope.ResourceField)] string Resource,
    [property: Orleans.Id(EventVectorAuthorizationScope.KindField)] EventSourceKind Kind)
{
    internal const string SerializerAlias = "keyload.core.event-vector-authorization-scope.v1";
    internal const int PartitionField = 0;
    internal const int ResourceField = 1;
    internal const int KindField = 2;
}
