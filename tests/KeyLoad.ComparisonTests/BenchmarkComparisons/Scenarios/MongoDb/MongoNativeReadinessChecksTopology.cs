using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Resolves only the selected real Aspire resources and one verified common native network.</summary>
internal static class MongoNativeReadinessChecksTopology
{
    internal static async Task<(string Image, string Network, string Module)> ReadAsync(ContainerResource[] resources, int count, CancellationToken token)
    {
        var nodes = resources.Where(resource => resource.Name.StartsWith(MongoNativeReadinessChecksProtocol.Prefix, StringComparison.Ordinal)
            && resource.Name != MongoNativeReadinessChecksProtocol.Bootstrap).OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
        MongoNativeReadinessChecksProtocol.Require(nodes.Select(node => node.Name).SequenceEqual(
            Enumerable.Range(0, count).Select(MongoNativeReadinessChecksProtocol.Node), StringComparer.Ordinal));
        var observations = new List<MongoNativeReadinessChecksIdentity>(count);
        string? image = null;
        foreach (var node in nodes)
        {
            var current = await InspectNodeAsync(node, token);
            MongoNativeReadinessChecksProtocol.Require(image is null || image == current.Image);
            image = current.Image;
            observations.Add(current);
        }
        var networks = observations.Select(observation => observation.Networks.Keys.ToHashSet(StringComparer.Ordinal)).ToArray();
        var common = networks[0];
        foreach (var network in networks.Skip(1))
        {
            common.IntersectWith(network);
        }
        MongoNativeReadinessChecksProtocol.Require(common.Count == MongoNativeReadinessChecksProtocol.CommonNetworkCount);
        var bootstrap = resources.Single(resource => resource.Name == MongoNativeReadinessChecksProtocol.Bootstrap);
        var mount = bootstrap.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == MongoNativeReadinessChecksProtocol.ModuleTarget);
        MongoNativeReadinessChecksProtocol.Require(mount.Type == ContainerMountType.BindMount && mount.IsReadOnly
            && mount.Source is not null && File.Exists(mount.Source));
        return (image!, common.Single(), Path.GetFullPath(mount.Source!));
    }

    private static async Task<MongoNativeReadinessChecksIdentity> InspectNodeAsync(ContainerResource node, CancellationToken token)
    {
        var name = node.Annotations.OfType<ContainerNameAnnotation>().Single().Name;
        var annotation = node.Annotations.OfType<ContainerImageAnnotation>().Single();
        MongoNativeReadinessChecksProtocol.Require(MongoNativeReadinessChecksProtocol.Identifier(name)
            && name.StartsWith(MongoNativeReadinessChecksProtocol.Prefix, StringComparison.Ordinal)
            && annotation.SHA256 == BenchmarkResources.MongoDigest[MongoNativeReadinessChecksProtocol.DigestPrefixLength..]
            && node.TryGetContainerImageName(out _));
        _ = node.TryGetContainerImageName(out var image);
        var actual = await MongoNativeReadinessChecksDocker.InspectAsync(name, token);
        actual.RequireNode(name, image!, node.Name);
        return actual;
    }
}
