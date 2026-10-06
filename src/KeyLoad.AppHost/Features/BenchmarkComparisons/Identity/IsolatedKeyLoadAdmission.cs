using System.Globalization;
using KeyLoad;
using KeyLoad.Comparisons;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedKeyLoadAdmission
{
    private const string Prefix = "KeyLoad__HttpAdmission__";
    private const string SelectionPrefix = IsolatedKeyLoadAdmissionOptions.SectionName + "__";

    internal static void Apply(IResourceBuilder<ContainerResource> node, IOptions<HttpAdmissionLimits> http,
        IOptions<ReplicaReplayLimits> replay)
    {
        var limits = http.Value;
        Set(node, nameof(limits.MaxRequests), limits.MaxRequests);
        Set(node, nameof(limits.MaxTenantRequests), limits.MaxTenantRequests);
        Set(node, nameof(limits.MaxPrincipalRequests), limits.MaxPrincipalRequests);
        Set(node, nameof(limits.ReservedControlRequests), limits.ReservedControlRequests);
        Set(node, nameof(limits.MaxTenantControlRequests), limits.MaxTenantControlRequests);
        Set(node, nameof(limits.MaxPrincipalControlRequests), limits.MaxPrincipalControlRequests);
        Set(node, nameof(limits.MaxReservedBytes), limits.MaxReservedBytes);
        IsolatedKeyLoadReplayProfile.Apply(node, replay);
    }

    internal static void ForwardSelection(IResourceBuilder<ContainerResource> runner, IOptions<IsolatedKeyLoadAdmissionOptions> options)
    {
        var limits = options.Value;
        SetSelection(runner, nameof(limits.RequestsPerScope), limits.RequestsPerScope);
        SetSelection(runner, nameof(limits.ReservedBytes), limits.ReservedBytes);
    }

    private static void SetSelection(IResourceBuilder<ContainerResource> runner, string name, long value)
        => runner.WithEnvironment(SelectionPrefix + name, value.ToString(CultureInfo.InvariantCulture));

    private static void Set(IResourceBuilder<ContainerResource> node, string option, long value)
        => node.WithEnvironment(Prefix + option, value.ToString(CultureInfo.InvariantCulture));
}
