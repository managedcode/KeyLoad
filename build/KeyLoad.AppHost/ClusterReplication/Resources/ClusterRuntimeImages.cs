using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.ClusterReplication;

internal static class ClusterRuntimeImages
{
    internal static (LocalDevelopmentContainerImage? LocalImage,
        IReadOnlyDictionary<string, RuntimeContainerImage>? Images, RequestCqrsProbeProfile? Probes)
        Read(IDistributedApplicationBuilder builder, string root, bool ephemeral,
            int? benchmarkNodeCount, NativeCoverageRf3ClusterImage? coverageImage, string[] nodeNames)
    {
        const string MessageText = "Local image mode requires an ephemeral non-benchmark topology.";

        var localImage = LocalDevelopmentContainerImage.Read(builder);
        if (coverageImage is not null && localImage is not null)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        if (localImage is not null && (!ephemeral || benchmarkNodeCount is not null))
        {
            throw new InvalidOperationException(MessageText);
        }
        var runtime = AppHostOptionsRegistration.Get(builder);
        if (localImage is null && coverageImage is null && benchmarkNodeCount is null && !ephemeral
            && runtime.Images.Value.Server is null && !runtime.Control.Value.ProtocolCohortConfigured
            && !runtime.Control.Value.TwoRf3)
        {
            return (null, null, null);
        }
        var images = localImage is null && coverageImage is null
            ? ProtocolCohortImages.Read(builder, ephemeral, benchmarkNodeCount, nodeNames) : null;
        var probes = localImage is not null
            ? RequestCqrsProbeProfile.ReadLocal(builder, root, ephemeral, benchmarkNodeCount, localImage)
            : coverageImage is null
                ? RequestCqrsProbeProfile.Read(builder, root, ephemeral, benchmarkNodeCount, images!)
                : RequestCqrsProbeProfile.ReadCovered(builder, root, ephemeral, benchmarkNodeCount, coverageImage);
        return (localImage, images, probes);
    }
}
