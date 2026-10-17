namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record RequestCqrsProbeLoadedArm(RequestCqrsProbeArmRecord Record, byte[] ExactBytes);
