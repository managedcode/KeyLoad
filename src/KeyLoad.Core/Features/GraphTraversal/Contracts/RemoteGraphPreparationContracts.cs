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

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphTargetPreparationV1.Alias)]
internal sealed record RemoteGraphTargetPreparationV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphPreparationRequestV1 Request,
    [property: global::Orleans.Id(2)] RemoteGraphOwnerCutV1 Cut,
    [property: global::Orleans.Id(3)] long CollectionSchemaVersion,
    [property: global::Orleans.Id(4)] long? DocumentRevision)
{
    internal const string Alias = "keyload.core.remote-graph.targetpreparation.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphPreparationClaimsV1.Alias)]
internal sealed record RemoteGraphPreparationClaimsV1(
    [property: global::Orleans.Id(0)] string Purpose,
    [property: global::Orleans.Id(1)] RemoteGraphTargetPreparationV1 Preparation,
    [property: global::Orleans.Id(2)] string SourcePrincipalId,
    [property: global::Orleans.Id(3)] long SourcePolicyEpoch,
    [property: global::Orleans.Id(4)] string OriginalRequestDigest,
    [property: global::Orleans.Id(5)] DateTimeOffset ExpiresAt)
{
    internal const string Alias = "keyload.core.remote-graph.preparationclaims.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphVerifiedPreparationV1.Alias)]
internal sealed record RemoteGraphVerifiedPreparationV1(
    [property: global::Orleans.Id(0)] RemoteGraphTargetPreparationV1 Preparation,
    [property: global::Orleans.Id(1)] string Token)
{
    internal const string Alias = "keyload.core.remote-graph.verifiedpreparation.v1";
}
