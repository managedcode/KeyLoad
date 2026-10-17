using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record ConnectionProbeWitness(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string Voter,
    [property: JsonRequired] string SiloAddress,
    [property: JsonRequired] Guid ConnectionId,
    [property: JsonRequired] string ConnectionGrainType,
    [property: JsonRequired] string GrainId,
    [property: JsonRequired] string ActivationId,
    [property: JsonRequired] int SelectedActivationCount,
    [property: JsonRequired] int ClusterConnectionActivationCount,
    [property: JsonRequired] bool Closed);
