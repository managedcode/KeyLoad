using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests.OrleansExecution.Helpers;

internal static class RequestCqrsClusterNodeResources
{
    internal static ProjectResource[] Select(IDistributedApplicationTestingBuilder builder)
    {
        var selected = builder.Resources.OfType<ProjectResource>().Where(resource => IsNode(resource.Name))
            .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
        if (selected.Length != RequestCqrsClusterProtocol.NodeCount)
        {
            throw new InvalidOperationException("The actual Aspire model did not contain three execution project node resources.");
        }
        return selected;
    }

    private static bool IsNode(string name) => name is RequestCqrsClusterProtocol.Node1
        or RequestCqrsClusterProtocol.Node2 or RequestCqrsClusterProtocol.Node3;
}
