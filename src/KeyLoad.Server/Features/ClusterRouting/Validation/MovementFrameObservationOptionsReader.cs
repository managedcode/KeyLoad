namespace KeyLoad.Server.Features.ClusterRouting;

[ConfigurationBinding]
internal static class MovementFrameObservationOptionsReader
{
    private const int ConfigurationFieldCount = 3;
    private const int ExcessConfigurationField = 1;
    private const int NoConfigurationFields = 0;
    private const int FirstNestedField = 1;
    private const string EnabledSetting = "Enabled";
    private const string RootSetting = "Root";
    private const string SessionSetting = "SessionId";

    internal static MovementFrameObservationOptions Read(IConfiguration configuration, NodeOptions node)
    {
        var section = configuration.GetSection(MovementFrameObservationProtocol.ConfigurationSection);
        var fields = section.GetChildren().Take(ConfigurationFieldCount + ExcessConfigurationField).ToArray();
        if (fields.Length == NoConfigurationFields && section.Value is null)
        { return MovementFrameObservationOptions.Disabled; }
        if (section.Value is not null || fields.Length > ConfigurationFieldCount
            || fields.Any(field => field.Key is not (EnabledSetting or RootSetting or SessionSetting)
                || field.Value is null || field.GetChildren().Take(FirstNestedField).Any())
            || !bool.TryParse(section[EnabledSetting], out var enabled))
        { throw Invalid(); }
        var value = new MovementFrameObservationOptions(enabled, section[RootSetting], section[SessionSetting] ?? string.Empty);
        Validate(value, node);
        return value;
    }

    internal static void Validate(MovementFrameObservationOptions value, NodeOptions node)
    {
        if (!value.Enabled)
        {
            if (value.Root is not null || value.SessionId.Length != NoConfigurationFields)
            { throw Invalid(); }
            return;
        }
        MembershipAuthoritySettingsValidator.Validate(node.MembershipAuthority, node);
        if (value.Root != MovementFrameObservationProtocol.FixedRoot
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId)
            || !node.AllowPrivateNetworkHttp || node.BenchmarkTopology || node.RequestCqrsProbe.Enabled
            || node.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Proxy
            || !node.MembershipAuthority.RegisterPhysicalOwners || !node.MembershipAuthority.RemoteDocumentReads
            || !node.MembershipAuthority.RemotePartitionQueries)
        { throw Invalid(); }
    }

    private static InvalidOperationException Invalid() => new(MovementFrameObservationProtocol.Invalid);
}
