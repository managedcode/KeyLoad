namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedKeyLoadReplayProfile
{
    internal const int CriticalPerVoter = 16_384;
    internal const int ForwardPerVoter = 32_768;
    internal const int ReadBarrierPerVoter = 196_608;
    internal const int DataAppendPerVoter = 32_768;
    private const string Prefix = "KeyLoad__ReplayAdmission__";

    internal static void Apply(IResourceBuilder<ContainerResource> node)
    {
        Set(node, nameof(CriticalPerVoter), CriticalPerVoter);
        Set(node, nameof(ForwardPerVoter), ForwardPerVoter);
        Set(node, nameof(ReadBarrierPerVoter), ReadBarrierPerVoter);
        Set(node, nameof(DataAppendPerVoter), DataAppendPerVoter);
    }

    private static void Set(IResourceBuilder<ContainerResource> node, string option, int value)
        => node.WithEnvironment(Prefix + option, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
