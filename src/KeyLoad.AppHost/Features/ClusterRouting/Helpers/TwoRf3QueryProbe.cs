using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Reuses the exact three-voter signed controls only on the explicit first query owner.</summary>
internal static class TwoRf3QueryProbe
{
    private const int FirstProbeIndex = 0;
    private const string FirstVoter = "node1";
    private const string SecondVoter = "node2";
    private const string ThirdVoter = "node3";

    internal static RequestCqrsProbeProfile? Create(IDistributedApplicationBuilder builder, string root,
        RuntimeContainerImage? image, LocalDevelopmentContainerImage? local)
    {
        var control = AppHostOptionsRegistration.Get(builder).Control.Value;
        if (control.RequestProbe is null)
        { return null; }
        if (!control.TwoRf3 || local is not null || image is null
            || !control.RemoteDocumentReads
            || !control.RemotePartitionQueries)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        return RequestCqrsProbeProfile.Read(builder, root, ephemeral: true, benchmarkNodeCount: null,
            new Dictionary<string, RuntimeContainerImage>(StringComparer.Ordinal)
            { [FirstVoter] = image, [SecondVoter] = image, [ThirdVoter] = image });
    }
    internal static void Apply(RequestCqrsProbeProfile? probe,
        IReadOnlyList<IResourceBuilder<ContainerResource>> resources, IReadOnlyList<string> nodes)
    {
        if (probe is null)
        { return; }
        for (var index = FirstProbeIndex; index < TwoRf3ProfileProtocol.MembersPerGroup; index++)
        { probe.Apply(resources[index], nodes[index]); }
    }
}
