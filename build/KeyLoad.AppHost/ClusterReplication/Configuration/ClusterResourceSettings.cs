using System.Globalization;
using KeyLoad;
using KeyLoad.AppHost.Hosting;

internal static class ClusterResourceSettings
{
    private const string CommandEnvironment = "KeyLoad__CommandAdmission__";
    private const string HttpEnvironment = "KeyLoad__HttpAdmission__";
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
    internal static void Apply(IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> resource, string? containerUser)
    {
        KeyLoad.AppHost.Features.ClusterRouting.ClusterTelemetryResources.Apply(resource);
        var options = AppHostOptionsRegistration.Get(builder);
        resource.WithEnvironment(LogEnvironment, options.Cluster.Value.LoggingLevel);
        Copy(resource, options.CommandAdmission.Value, CommandOptions, CommandEnvironment);
        Copy(resource, options.HttpAdmission.Value, HttpOptions, HttpEnvironment);
        if (containerUser is not null)
        {
            resource.WithContainerRuntimeArgs(ContainerUserArgument, containerUser);
        }
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
