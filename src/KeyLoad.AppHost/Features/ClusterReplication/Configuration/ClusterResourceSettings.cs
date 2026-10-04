using KeyLoad;
using KeyLoad.Orleans;

internal static class ClusterResourceSettings
{
    private const string SnapshotConfiguration = "KeyLoad:SnapshotThreshold";
    private const string SnapshotEnvironment = "KeyLoad__SnapshotThreshold";
    private const string DefaultSnapshotThreshold = "1024";
    private const string CommandConfiguration = "KeyLoad:CommandAdmission:";
    private const string CommandEnvironment = "KeyLoad__CommandAdmission__";
    private const string HttpConfiguration = "KeyLoad:HttpAdmission:";
    private const string HttpEnvironment = "KeyLoad__HttpAdmission__";
    private const string ReplayConfiguration = "KeyLoad:ReplayAdmission:";
    private const string ReplayEnvironment = "KeyLoad__ReplayAdmission__";
    private const string LogEnvironment = "Logging__LogLevel__Default";
    private const string LogLevel = "Warning";
    private const string ContainerUserArgument = "--user";
    private static readonly string[] CommandOptions =
    [
        nameof(CommandAdmissionLimits.MaxCommands), nameof(CommandAdmissionLimits.MaxRetainedBytes),
        nameof(CommandAdmissionLimits.MaxTenantCommands), nameof(CommandAdmissionLimits.MaxPrincipalCommands),
        nameof(CommandAdmissionLimits.ReservedControlCommands), nameof(CommandAdmissionLimits.ReservedControlBytes),
        nameof(CommandAdmissionLimits.MaxControlPayloadBytes), nameof(CommandAdmissionLimits.MaxTenantControlCommands),
        nameof(CommandAdmissionLimits.MaxPrincipalControlCommands)
    ];
    private static readonly string[] HttpOptions =
    [
        nameof(HttpAdmissionLimits.MaxRequests), nameof(HttpAdmissionLimits.MaxReservedBytes),
        nameof(HttpAdmissionLimits.MaxTenantRequests), nameof(HttpAdmissionLimits.MaxPrincipalRequests),
        nameof(HttpAdmissionLimits.ReservedControlRequests), nameof(HttpAdmissionLimits.ReservedControlBytes),
        nameof(HttpAdmissionLimits.MaxTenantControlRequests), nameof(HttpAdmissionLimits.MaxPrincipalControlRequests),
        nameof(HttpAdmissionLimits.MaxBodyBytes), nameof(HttpAdmissionLimits.MaxControlBodyBytes),
        nameof(HttpAdmissionLimits.HeavyReadReservedBytes), nameof(HttpAdmissionLimits.OtherReservedBytes)
    ];
    private static readonly string[] ReplayOptions =
    [
        nameof(ReplicaReplayLimits.CriticalPerVoter), nameof(ReplicaReplayLimits.ForwardPerVoter),
        nameof(ReplicaReplayLimits.ReadBarrierPerVoter), nameof(ReplicaReplayLimits.DataAppendPerVoter)
    ];

    internal static void Apply(IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> resource, string? containerUser)
    {
        resource.WithEnvironment(SnapshotEnvironment, builder.Configuration[SnapshotConfiguration] ?? DefaultSnapshotThreshold)
            .WithEnvironment(LogEnvironment, LogLevel);
        Copy(builder, resource, CommandOptions, CommandConfiguration, CommandEnvironment);
        Copy(builder, resource, HttpOptions, HttpConfiguration, HttpEnvironment);
        Copy(builder, resource, ReplayOptions, ReplayConfiguration, ReplayEnvironment);
        if (containerUser is not null)
        { resource.WithContainerRuntimeArgs(ContainerUserArgument, containerUser); }
    }

    private static void Copy(IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> resource,
        string[] options, string configurationPrefix, string environmentPrefix)
    {
        foreach (var option in options)
        {
            if (builder.Configuration[configurationPrefix + option] is { } value)
            { resource.WithEnvironment(environmentPrefix + option, value); }
        }
    }
}
