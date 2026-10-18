using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal static class RequestCqrsClusterNodeResources
{
    internal static ContainerResource[] Select(IDistributedApplicationTestingBuilder builder)
    {
        var selected = builder.Resources.OfType<ContainerResource>().Where(resource => IsNode(resource.Name))
            .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
        if (selected.Length != RequestCqrsClusterProtocol.NodeCount)
        {
            throw new InvalidOperationException("The actual Aspire model did not contain three C1 node containers.");
        }
        return selected;
    }

    private static bool IsNode(string name) => name is RequestCqrsClusterProtocol.Node1
        or RequestCqrsClusterProtocol.Node2 or RequestCqrsClusterProtocol.Node3;
}
