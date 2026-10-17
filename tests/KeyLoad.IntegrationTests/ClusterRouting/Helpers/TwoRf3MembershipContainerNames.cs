using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Reads the exact six owned resource names from native Aspire container annotations.</summary>
internal static class TwoRf3MembershipContainerNames
{
    internal static Dictionary<string, string> Read(IEnumerable<ContainerResource> resources)
    {
        var names = resources.Where(resource => TwoRf3MembershipProtocol.Nodes.Contains(resource.Name,
                StringComparer.Ordinal))
            .ToDictionary(resource => resource.Name,
                resource => resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name,
                StringComparer.Ordinal);
        if (names.Count != TwoRf3MembershipProtocol.NodeCount
            || TwoRf3MembershipProtocol.Nodes.Any(node => !names.ContainsKey(node)))
        {
            throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch);
        }
        return names;
    }
}
