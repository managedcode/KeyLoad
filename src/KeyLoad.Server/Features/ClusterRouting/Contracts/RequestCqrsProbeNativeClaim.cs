namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Original signed producer claim bound to the admitted native request and configured owner.</summary>
/// <param name="SessionId">Fixture-owned original session identity.</param>
/// <param name="ArmId">Original admitted arm identity.</param>
/// <param name="Identity">Exact native request and connection identity.</param>
/// <param name="Owner">Configured physical owner including placement epoch.</param>
/// <param name="Voter">Configured native voter name.</param>
/// <param name="SiloAddress">Full native silo address including generation.</param>
/// <param name="ArmSha256">Original arm byte digest.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RequestCqrsProbeNativeProtocol.ClaimAlias)]
internal sealed record RequestCqrsProbeNativeClaim(
    [property: global::Orleans.Id(0)] string SessionId,
    [property: global::Orleans.Id(1)] Guid ArmId,
    [property: global::Orleans.Id(2)] RequestCqrsProbeNativeIdentity Identity,
    [property: global::Orleans.Id(3)] PhysicalShardRecord Owner,
    [property: global::Orleans.Id(4)] string Voter,
    [property: global::Orleans.Id(5)] string SiloAddress,
    [property: global::Orleans.Id(6)] string ArmSha256);
