using System.Globalization;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedKeyLoadAdmission
{
    private const string Prefix = "KeyLoad__HttpAdmission__";

    internal static void Apply(IResourceBuilder<ContainerResource> node)
    {
        var limits = global::KeyLoad.Comparisons.IsolatedKeyLoadAdmissionProfile.Limits;
        Set(node, nameof(limits.MaxRequests), limits.MaxRequests);
        Set(node, nameof(limits.MaxTenantRequests), limits.MaxTenantRequests);
        Set(node, nameof(limits.MaxPrincipalRequests), limits.MaxPrincipalRequests);
        Set(node, nameof(limits.ReservedControlRequests), limits.ReservedControlRequests);
        Set(node, nameof(limits.MaxTenantControlRequests), limits.MaxTenantControlRequests);
        Set(node, nameof(limits.MaxPrincipalControlRequests), limits.MaxPrincipalControlRequests);
        Set(node, nameof(limits.MaxReservedBytes), limits.MaxReservedBytes);
        IsolatedKeyLoadReplayProfile.Apply(node);
    }

    private static void Set(IResourceBuilder<ContainerResource> node, string option, long value)
        => node.WithEnvironment(Prefix + option, value.ToString(CultureInfo.InvariantCulture));
}
