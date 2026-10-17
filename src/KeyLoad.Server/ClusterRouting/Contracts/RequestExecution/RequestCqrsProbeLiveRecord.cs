using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Private diagnostic identity only; never request/effect authority.</summary>
internal readonly record struct RequestCqrsProbeLiveRecord(
    [property: JsonRequired] int Version, [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId, [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId, [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string Voter, [property: JsonRequired] string SiloAddress,
    [property: JsonRequired] string GrainDigest, [property: JsonRequired] string ActivationId,
    [property: JsonRequired] int Step, [property: JsonRequired] Guid Nonce,
    [property: JsonRequired] string PredecessorSha256, [property: JsonRequired] string ChallengeSha256);
