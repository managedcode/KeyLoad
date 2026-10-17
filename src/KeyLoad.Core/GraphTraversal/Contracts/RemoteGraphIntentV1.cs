namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphIntentV1.Alias)]
internal sealed record RemoteGraphIntentV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] GraphCrossPartitionDeliveryIntentV1 Content,
    [property: global::Orleans.Id(2)] PhysicalShardRecord SourceOwner,
    [property: global::Orleans.Id(3)] RegisteredPhysicalOwnerV1 DestinationOwner,
    [property: global::Orleans.Id(4)] CommitToken SourceCommit,
    [property: global::Orleans.Id(5)] Guid SourceCommandId)
{
    internal const string Alias = "keyload.core.remote-graph.intent.v1";
    internal const int ResourceLifetimesField = 6;
    [global::Orleans.Id(ResourceLifetimesField), System.Text.Json.Serialization.JsonIgnore]
    public System.Collections.Immutable.ImmutableArray<ResourceLifetimeV1> ResourceLifetimes
    {
        get; init;
    }
}
