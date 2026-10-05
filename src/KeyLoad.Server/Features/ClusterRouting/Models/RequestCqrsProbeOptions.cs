namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record RequestCqrsProbeOptions(bool Enabled, string? Root, string SessionId, string DiscoveryCaptureMode = RequestCqrsProbeProtocol.DiscoveryCaptureDisabled);
