using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal static class RequestCqrsRf3NodeResources
{
    internal static ContainerResource[] Select(IDistributedApplicationTestingBuilder builder)
    {
        var selected = builder.Resources.OfType<ContainerResource>().Where(resource => IsNode(resource.Name))
            .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
        if (selected.Length != RequestCqrsRf3Protocol.NodeCount)
        { throw new InvalidOperationException("The actual Aspire model did not contain three C1 node containers."); }
        return selected;
    }

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;
}
