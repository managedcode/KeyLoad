using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Reuses the exact three-voter signed controls only on the explicit first query owner.</summary>
internal static class TwoRf3QueryProbe
{
    private const int FirstProbeIndex = 0;

    internal static RequestCqrsProbeProfile? Create(IDistributedApplicationBuilder builder, string root,
        RuntimeContainerImage? image, LocalDevelopmentContainerImage? local)
    {
        var control = AppHostOptionsRegistration.Get(builder).Control.Value;
        if (control.RequestProbe is null)
        {
            return null;
        }
        if (local is not null && image is null && control.NativeDiscoveryOmission is not null)
        {
            return RequestCqrsProbeProfile.ReadDiagnosticSix(builder, root, image, local);
        }
        if (local is not null && image is null && control.ProtectedMovementProbe)
        {
            return RequestCqrsProbeProfile.ReadDiagnosticSix(builder, root, image, local);
        }
        if (local is not null && image is null && control.EventVectorReservedProbe)
        {
            return RequestCqrsProbeProfile.ReadDiagnosticSix(builder, root, image, local);
        }
        if (control.TwoRf3 && !control.RemoteDocumentReads && !control.RemotePartitionQueries)
        {
            return RequestCqrsProbeProfile.ReadDiagnosticSix(builder, root, image, local);
        }
        if (!control.TwoRf3 || local is not null || image is null
            || !control.RemoteDocumentReads
            || !control.RemotePartitionQueries)
        {
            throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid);
        }
        return RequestCqrsProbeProfile.ReadDiagnosticSix(builder, root, image, local);
    }
    internal static void Apply(RequestCqrsProbeProfile? probe,
        IReadOnlyList<IResourceBuilder<ContainerResource>> resources, IReadOnlyList<string> nodes)
    {
        if (probe is null)
        {
            return;
        }
        if ((probe.MemberCount != TwoRf3ProfileProtocol.MembersPerGroup
            && probe.MemberCount != TwoRf3ProfileProtocol.TotalNodes)
            || resources.Count != TwoRf3ProfileProtocol.TotalNodes
            || nodes.Count != TwoRf3ProfileProtocol.TotalNodes)
        {
            throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid);
        }
        for (var index = FirstProbeIndex; index < probe.MemberCount; index++)
        {
            probe.Apply(resources[index], nodes[index]);
        }
    }
}
