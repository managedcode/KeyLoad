using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

[ConfigurationBinding]
internal static class NativeDiscoveryOmissionOptionsReader
{
    internal static NativeDiscoveryOmissionOptions Read(IConfiguration configuration, NodeOptions node)
    {
        var section = configuration.GetSection(NativeDiscoveryOmissionProtocol.Section);
        var fields = section.GetChildren().Take(NativeDiscoveryOmissionProtocol.SettingsCount + NativeDiscoveryOmissionProtocol.ExcessEntry).ToArray();
        if (section.Value is null && fields.Length == NativeDiscoveryOmissionProtocol.EmptyCount)
        { return NativeDiscoveryOmissionOptions.Disabled; }
        if (section.Value is not null || fields.Length > NativeDiscoveryOmissionProtocol.SettingsCount
            || fields.Any(field => field.Key is not (NativeDiscoveryOmissionProtocol.Enabled
                or NativeDiscoveryOmissionProtocol.Root or NativeDiscoveryOmissionProtocol.SessionId)
                || field.Value is null || field.GetChildren().Take(NativeDiscoveryOmissionProtocol.ExcessEntry).Any())
            || !bool.TryParse(section[NativeDiscoveryOmissionProtocol.Enabled], out var enabled))
        { throw Invalid(); }
        var result = new NativeDiscoveryOmissionOptions(enabled, section[NativeDiscoveryOmissionProtocol.Root],
            section[NativeDiscoveryOmissionProtocol.SessionId] ?? string.Empty);
        Validate(result, node);
        return result;
    }

    internal static void Validate(NativeDiscoveryOmissionOptions options, NodeOptions node)
    {
        if (!options.Enabled)
        {
            if (options.Root is not null || options.SessionId.Length != NativeDiscoveryOmissionProtocol.EmptyCount)
            { throw Invalid(); }
            return;
        }
        if (options.Root != NativeDiscoveryOmissionProtocol.FixedRoot
            || !RequestCqrsProbeOptionsReader.IsSessionId(options.SessionId)
            || !node.RequestCqrsProbe.Enabled || !node.AllowPrivateNetworkHttp
            || !node.MembershipAuthority.RemoteDocumentReads || !node.MembershipAuthority.RemotePartitionQueries
            || node.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority)
        { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new(NativeDiscoveryOmissionProtocol.Invalid);
}
