namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphPreparationRequestV1.Alias)]
internal sealed record RemoteGraphPreparationRequestV1(
    [property: global::Orleans.Id(0)] Guid OriginalCommandId,
    [property: global::Orleans.Id(1)] string PrincipalId,
    [property: global::Orleans.Id(2)] PartitionRef SourcePartition,
    [property: global::Orleans.Id(3)] EntityRef Destination,
    [property: global::Orleans.Id(4)] string Graph,
    [property: global::Orleans.Id(5)] bool Deleted,
    [property: global::Orleans.Id(6)] PhysicalShardRecord SourceOwner,
    [property: global::Orleans.Id(7)] RegisteredPhysicalOwnerV1 DestinationOwner)
{
    internal const string Alias = "keyload.core.remote-graph.preparationrequest.v1";
}
