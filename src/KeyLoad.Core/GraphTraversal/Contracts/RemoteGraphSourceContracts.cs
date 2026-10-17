using System.Collections.Immutable;

namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphSourceIdentityV1.Alias)]
internal sealed record RemoteGraphSourceIdentityV1(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] PartitionRef SourcePartition,
    [property: global::Orleans.Id(2)] UpsertEdge? Upsert,
    [property: global::Orleans.Id(3)] DeleteEdge? Delete)
{
    internal const string Alias = "keyload.core.remote-graph.sourceidentity.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphSourceMutationV1.Alias)]
internal sealed record RemoteGraphSourceMutationV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphSourceIdentityV1 Original,
    [property: global::Orleans.Id(2)] long ExpectedOwnerRevision,
    [property: global::Orleans.Id(3)] ImmutableArray<RemoteGraphVerifiedPreparationV1> Preparations,
    [property: global::Orleans.Id(4)] EdgeRecord? PreviousEdge)
{
    internal const string Alias = "keyload.core.remote-graph.sourcemutation.v1";
}

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

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphSourceResultV1.Alias)]
internal sealed record RemoteGraphSourceResultV1(
    [property: global::Orleans.Id(0)] CommitReceipt Receipt,
    [property: global::Orleans.Id(1)] ImmutableArray<RemoteGraphIntentV1> Intents,
    [property: global::Orleans.Id(2)] RemoteGraphSourceMutationV1 Original)
{
    internal const string Alias = "keyload.core.remote-graph.sourceresult.v1";
}
