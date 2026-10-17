namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphTargetPreparationV1.Alias)]
internal sealed record RemoteGraphTargetPreparationV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphPreparationRequestV1 Request,
    [property: global::Orleans.Id(2)] RemoteGraphOwnerCutV1 Cut,
    [property: global::Orleans.Id(3)] long CollectionSchemaVersion,
    [property: global::Orleans.Id(4)] long? DocumentRevision)
{
    internal const string Alias = "keyload.core.remote-graph.targetpreparation.v1";
    internal const int ResourceLifetimesField = 5;
    [global::Orleans.Id(ResourceLifetimesField), System.Text.Json.Serialization.JsonIgnore]
    public System.Collections.Immutable.ImmutableArray<ResourceLifetimeV1> ResourceLifetimes
    {
        get; init;
    }
}
