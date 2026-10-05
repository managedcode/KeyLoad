using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record RequestCqrsProbeClaim(RequestCqrsProbeLoadedArm Arm, GrainRequestProbeIdentity Identity);
