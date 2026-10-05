using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeOptionsReader
{
    private const int MaximumConfigurationKeys = 4;

    internal static RequestCqrsProbeOptions Read(IConfiguration configuration, ReplicaConfiguration replica,
        bool allowPrivateNetworkHttp)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(replica);
        var section = configuration.GetSection(RequestCqrsProbeProtocol.ConfigurationSection);
        if (section.Value is not null)
        { throw Invalid(); }
        var children = section.GetChildren().Take(MaximumConfigurationKeys + 1).ToArray();
        if (children.Length == 0)
        {
            var disabled = new RequestCqrsProbeOptions(false, null, string.Empty, RequestCqrsProbeProtocol.DiscoveryCaptureDisabled);
            Validate(disabled, replica, allowPrivateNetworkHttp);
            return disabled;
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (children.Length > MaximumConfigurationKeys || children.Any(child =>
            !keys.Add(child.Key) || child.Key is not (RequestCqrsProbeProtocol.EnabledSetting
                or RequestCqrsProbeProtocol.RootSetting or RequestCqrsProbeProtocol.SessionSetting
                or RequestCqrsProbeProtocol.DiscoveryCaptureSetting)
                || child.Value is null || child.GetChildren().Take(1).Any()))
        { throw Invalid(); }
        var enabledValue = section[RequestCqrsProbeProtocol.EnabledSetting];
        if (!bool.TryParse(enabledValue, out var enabled))
        { throw Invalid(); }
        var rootConfigured = children.Any(child => child.Key == RequestCqrsProbeProtocol.RootSetting);
        var options = new RequestCqrsProbeOptions(enabled, section[RequestCqrsProbeProtocol.RootSetting],
            section[RequestCqrsProbeProtocol.SessionSetting] ?? string.Empty,
            section[RequestCqrsProbeProtocol.DiscoveryCaptureSetting] ?? RequestCqrsProbeProtocol.DiscoveryCaptureDisabled);
        Validate(options, replica, allowPrivateNetworkHttp, rootConfigured);
        return options;
    }

    internal static void Validate(RequestCqrsProbeOptions options, ReplicaConfiguration replica,
        bool allowPrivateNetworkHttp, bool rootConfigured = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(replica);
        if (!options.Enabled)
        {
            if (rootConfigured || options.Root is not null || options.SessionId.Length != 0
                || options.DiscoveryCaptureMode != RequestCqrsProbeProtocol.DiscoveryCaptureDisabled)
            { throw Invalid(); }
            return;
        }
        replica.Validate();
        if (options.Root != RequestCqrsProbeProtocol.FixedRoot || string.IsNullOrWhiteSpace(options.SessionId)
            || !IsSessionId(options.SessionId)
            || options.DiscoveryCaptureMode is not (RequestCqrsProbeProtocol.DiscoveryCaptureDisabled
                or RequestCqrsProbeProtocol.MixedInterface3Capture)
            || replica.BenchmarkTopology || replica.VoterIds.Length != 3
            || options.DiscoveryCaptureMode == RequestCqrsProbeProtocol.MixedInterface3Capture
                && replica.LocalId != replica.VoterIds[0]
            || !allowPrivateNetworkHttp || replica.VoterIds.Any(voter => !IsPrivateNetworkOrigin(voter)))
        { throw Invalid(); }
    }

    internal static bool IsSessionId(string? value)
        => Guid.TryParseExact(value, "N", out var session) && session != Guid.Empty
            && string.Equals(value, session.ToString("N"), StringComparison.Ordinal);

    private static bool IsPrivateNetworkOrigin(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var origin)
            && origin.Scheme == Uri.UriSchemeHttp && !origin.IsLoopback
            && string.IsNullOrEmpty(origin.UserInfo) && origin.AbsolutePath == "/"
            && string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment);

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidOptions);
}
