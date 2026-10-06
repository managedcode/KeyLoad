using System.Globalization;
using KeyLoad;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Orleans;

internal static class ClusterResourceSettings
{
    private const string SnapshotEnvironment = "KeyLoad__SnapshotThreshold";
    private const string CommandEnvironment = "KeyLoad__CommandAdmission__";
    private const string HttpEnvironment = "KeyLoad__HttpAdmission__";
    private const string ReplayEnvironment = "KeyLoad__ReplayAdmission__";
    private const string LogEnvironment = "Logging__LogLevel__Default";
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
        var options = AppHostOptionsRegistration.Get(builder);
        resource.WithEnvironment(SnapshotEnvironment, options.Cluster.Value.SnapshotThreshold.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(LogEnvironment, options.Cluster.Value.LoggingLevel);
        Copy(resource, options.CommandAdmission.Value, CommandOptions, CommandEnvironment);
        Copy(resource, options.HttpAdmission.Value, HttpOptions, HttpEnvironment);
        Copy(resource, options.ReplayAdmission.Value, ReplayOptions, ReplayEnvironment);
        if (containerUser is not null)
        { resource.WithContainerRuntimeArgs(ContainerUserArgument, containerUser); }
    }

    private static void Copy<T>(IResourceBuilder<ContainerResource> resource, T snapshot,
        string[] options, string environmentPrefix) where T : class
    {
        foreach (var option in options)
        {
            var value = typeof(T).GetProperty(option)!.GetValue(snapshot);
            resource.WithEnvironment(environmentPrefix + option, Convert.ToString(value, CultureInfo.InvariantCulture));
        }
    }
}
