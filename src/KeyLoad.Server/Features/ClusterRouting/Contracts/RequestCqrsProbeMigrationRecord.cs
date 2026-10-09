using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Private bounded identity only, not a routing/effect capability.</summary>
internal readonly record struct RequestCqrsProbeMigrationRecord(
    [property: JsonRequired] int Version, [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId, [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId, [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string Voter, [property: JsonRequired] string SiloAddress,
    [property: JsonRequired] string GrainDigest, [property: JsonRequired] string ActivationId,
    [property: JsonRequired] string TargetVoter, [property: JsonRequired] string TargetSiloAddress);
