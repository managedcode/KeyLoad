namespace KeyLoad.AppHost.Features.ClusterRouting;

internal sealed record RequestCqrsProbeOwnerRecord(int Version, string Kind, string SessionId, string Voter);
