using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Validates the exact native project silo model selected by the functional fixture.</summary>
internal static class FunctionalGateProjectModel
{
    internal static string[] ReadResourceNames(IDistributedApplicationTestingBuilder ownedBuilder, string[] nodeNames)
    {
        var names = ownedBuilder.Resources.OfType<ProjectResource>()
            .Where(resource => resource.Name.StartsWith(ClusterFixtureProtocol.NodePrefix, StringComparison.Ordinal))
            .Select(resource => resource.Name).ToArray();
        if (!names.Order(StringComparer.Ordinal).SequenceEqual(nodeNames.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The functional Aspire model must expose exactly its requested native silos.");
        }
        return names;
    }
}
