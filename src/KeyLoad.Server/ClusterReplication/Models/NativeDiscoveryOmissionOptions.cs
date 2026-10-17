namespace KeyLoad.Server;

internal sealed record NativeDiscoveryOmissionOptions(bool Enabled, string? Root, string SessionId)
{
    internal static NativeDiscoveryOmissionOptions Disabled { get; } = new(false, null, string.Empty);
}
