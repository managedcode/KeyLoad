using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Resolves only the selected real Aspire resources and one verified common execution network.</summary>
internal static class MongoReadinessChecksTopology
{
    internal static async Task<(string Image, string Network, string Module)> ReadAsync(ContainerResource[] resources, int count, CancellationToken token)
    {
        var nodes = resources.Where(resource => resource.Name.StartsWith(MongoReadinessChecksProtocol.Prefix, StringComparison.Ordinal)
            && resource.Name != MongoReadinessChecksProtocol.Bootstrap).OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
        MongoReadinessChecksProtocol.Require(nodes.Select(node => node.Name).SequenceEqual(
            Enumerable.Range(0, count).Select(MongoReadinessChecksProtocol.Node), StringComparer.Ordinal));
        var observations = new List<MongoReadinessChecksIdentity>(count);
        string? image = null;
        foreach (var node in nodes)
        {
            var current = await InspectNodeAsync(node, token);
            MongoReadinessChecksProtocol.Require(image is null || image == current.Image);
            image = current.Image;
            observations.Add(current);
        }
        var networks = observations.Select(observation => observation.Networks.Keys.ToHashSet(StringComparer.Ordinal)).ToArray();
        var common = networks[0];
        foreach (var network in networks.Skip(1))
        {
            common.IntersectWith(network);
        }
        MongoReadinessChecksProtocol.Require(common.Count == MongoReadinessChecksProtocol.CommonNetworkCount);
        var bootstrap = resources.Single(resource => resource.Name == MongoReadinessChecksProtocol.Bootstrap);
        var mount = bootstrap.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == MongoReadinessChecksProtocol.ModuleTarget);
        MongoReadinessChecksProtocol.Require(mount.Type == ContainerMountType.BindMount && mount.IsReadOnly
            && mount.Source is not null && File.Exists(mount.Source));
        return (image!, common.Single(), Path.GetFullPath(mount.Source!));
    }

    private static async Task<MongoReadinessChecksIdentity> InspectNodeAsync(ContainerResource node, CancellationToken token)
    {
        var name = node.Annotations.OfType<ContainerNameAnnotation>().Single().Name;
        var annotation = node.Annotations.OfType<ContainerImageAnnotation>().Single();
        MongoReadinessChecksProtocol.Require(MongoReadinessChecksProtocol.Identifier(name)
            && name.StartsWith(MongoReadinessChecksProtocol.Prefix, StringComparison.Ordinal)
            && annotation.SHA256 == BenchmarkResources.MongoDigest[MongoReadinessChecksProtocol.DigestPrefixLength..]
            && node.TryGetContainerImageName(out _));
        _ = node.TryGetContainerImageName(out var image);
        var actual = await MongoReadinessChecksDocker.InspectAsync(name, token);
        actual.RequireNode(name, image!, node.Name);
        return actual;
    }
}
