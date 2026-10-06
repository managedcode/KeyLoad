using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedKeyLoadReplayProfile
{
    private const string Prefix = "KeyLoad__ReplayAdmission__";

    internal static void Apply(IResourceBuilder<ContainerResource> node, IOptions<ReplicaReplayLimits> options)
    {
        var limits = options.Value;
        Set(node, nameof(limits.CriticalPerVoter), limits.CriticalPerVoter);
        Set(node, nameof(limits.ForwardPerVoter), limits.ForwardPerVoter);
        Set(node, nameof(limits.ReadBarrierPerVoter), limits.ReadBarrierPerVoter);
        Set(node, nameof(limits.DataAppendPerVoter), limits.DataAppendPerVoter);
    }

    private static void Set(IResourceBuilder<ContainerResource> node, string option, int value)
        => node.WithEnvironment(Prefix + option, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
