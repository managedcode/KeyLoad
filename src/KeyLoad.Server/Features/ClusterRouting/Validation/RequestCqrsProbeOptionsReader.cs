using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

[ConfigurationBinding]
internal static class RequestCqrsProbeOptionsReader
{
    private const string IsSessionIdCompactIdentityFormat = "N";

    private const string RootPath = "/";

    private const int MaximumConfigurationKeys = 4;

    internal static RequestCqrsProbeOptions Read(IConfiguration configuration, ReplicaConfiguration replica,
        bool allowPrivateNetworkHttp)
    {
        const int MaximumConfigurationKeysStep = 1;
        const int EmptyChildrenLength = 0;
        const int CountSingleItemCount = 1;

        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(replica);
        var section = configuration.GetSection(RequestCqrsProbeProtocol.ConfigurationSection);
        if (section.Value is not null)
        { throw Invalid(); }
        var children = section.GetChildren().Take(MaximumConfigurationKeys + MaximumConfigurationKeysStep).ToArray();
        if (children.Length == EmptyChildrenLength)
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
                || child.Value is null || child.GetChildren().Take(CountSingleItemCount).Any()))
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
        const int EmptySessionIdLength = 0;
        const int EmptyVoterIdsLength = 3;
        const int IndexEmptyCount = 0;

        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(replica);
        if (!options.Enabled)
        {
            if (rootConfigured || options.Root is not null || options.SessionId.Length != EmptySessionIdLength
                || options.DiscoveryCaptureMode != RequestCqrsProbeProtocol.DiscoveryCaptureDisabled)
            { throw Invalid(); }
            return;
        }
        replica.Validate();
        if (options.Root != RequestCqrsProbeProtocol.FixedRoot || string.IsNullOrWhiteSpace(options.SessionId)
            || !IsSessionId(options.SessionId)
            || options.DiscoveryCaptureMode is not (RequestCqrsProbeProtocol.DiscoveryCaptureDisabled
                or RequestCqrsProbeProtocol.MixedInterface3Capture)
            || replica.BenchmarkTopology || replica.VoterIds.Length != EmptyVoterIdsLength
            || options.DiscoveryCaptureMode == RequestCqrsProbeProtocol.MixedInterface3Capture
                && replica.LocalId != replica.VoterIds[IndexEmptyCount]
            || !allowPrivateNetworkHttp || replica.VoterIds.Any(voter => !IsPrivateNetworkOrigin(voter)))
        { throw Invalid(); }
    }

    internal static bool IsSessionId(string? value)
        => Guid.TryParseExact(value, IsSessionIdCompactIdentityFormat, out var session) && session != Guid.Empty
            && string.Equals(value, session.ToString(RequestCqrsProbeProtocol.SessionIdFormat), StringComparison.Ordinal);

    private static bool IsPrivateNetworkOrigin(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var origin)
            && origin.Scheme == Uri.UriSchemeHttp && !origin.IsLoopback
            && string.IsNullOrEmpty(origin.UserInfo) && origin.AbsolutePath == RootPath
            && string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment);

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidOptions);
}
