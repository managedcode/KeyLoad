namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record MovementFrameObservationOptions(bool Enabled, string? Root, string SessionId)
{
    internal static MovementFrameObservationOptions Disabled { get; } = new(false, null, string.Empty);
}
