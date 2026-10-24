using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class IsolatedKeyLoadAdmission
{
    private const string Prefix = "KeyLoad__HttpAdmission__";
    private const string SelectionPrefix = IsolatedKeyLoadAdmissionOptions.SectionName + "__";

    internal static void Apply(IResourceBuilder<ProjectResource> node, IOptions<HttpAdmissionLimits> http)
    {
        var limits = http.Value;
        Set(node, nameof(limits.MaxRequests), limits.MaxRequests);
        Set(node, nameof(limits.MaxTenantRequests), limits.MaxTenantRequests);
        Set(node, nameof(limits.MaxPrincipalRequests), limits.MaxPrincipalRequests);
        Set(node, nameof(limits.ReservedControlRequests), limits.ReservedControlRequests);
        Set(node, nameof(limits.MaxTenantControlRequests), limits.MaxTenantControlRequests);
        Set(node, nameof(limits.MaxPrincipalControlRequests), limits.MaxPrincipalControlRequests);
        Set(node, nameof(limits.MaxReservedBytes), limits.MaxReservedBytes);
    }

    internal static void ForwardSelection(LoadTestClientSettings client, IOptions<IsolatedKeyLoadAdmissionOptions> options)
    {
        var limits = options.Value;
        SetSelection(client, nameof(limits.RequestsPerScope), limits.RequestsPerScope);
        SetSelection(client, nameof(limits.ReservedBytes), limits.ReservedBytes);
        if (limits.DocumentWorkload)
        {
            client.Set(SelectionPrefix + nameof(limits.DocumentWorkload), bool.TrueString);
        }
    }

    private static void SetSelection(LoadTestClientSettings client, string name, long value)
        => client.Set(SelectionPrefix + name, value.ToString(CultureInfo.InvariantCulture));

    private static void Set(IResourceBuilder<ProjectResource> node, string option, long value)
        => node.WithEnvironment(Prefix + option, value.ToString(CultureInfo.InvariantCulture));
}
