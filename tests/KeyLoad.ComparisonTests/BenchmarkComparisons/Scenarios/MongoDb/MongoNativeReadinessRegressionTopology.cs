using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Resolves only the selected real Aspire resources and one verified common native network.</summary>
internal static class MongoNativeReadinessRegressionTopology
{
    internal static async Task<(string Image, string Network, string Module)> ReadAsync(ContainerResource[] resources, int count, CancellationToken token)
    {
        var nodes = resources.Where(resource => resource.Name.StartsWith(MongoNativeReadinessRegressionProtocol.Prefix, StringComparison.Ordinal)
            && resource.Name != MongoNativeReadinessRegressionProtocol.Bootstrap).OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
        MongoNativeReadinessRegressionProtocol.Require(nodes.Select(node => node.Name).SequenceEqual(
            Enumerable.Range(0, count).Select(MongoNativeReadinessRegressionProtocol.Node), StringComparer.Ordinal));
        var observations = new List<MongoNativeReadinessRegressionIdentity>(count);
        string? image = null;
        foreach (var node in nodes)
        {
            var current = await InspectNodeAsync(node, token);
            MongoNativeReadinessRegressionProtocol.Require(image is null || image == current.Image);
            image = current.Image;
            observations.Add(current);
        }
        var networks = observations.Select(observation => observation.Networks.Keys.ToHashSet(StringComparer.Ordinal)).ToArray();
        var common = networks[0];
        foreach (var network in networks.Skip(1))
        {
            common.IntersectWith(network);
        }
        MongoNativeReadinessRegressionProtocol.Require(common.Count == MongoNativeReadinessRegressionProtocol.CommonNetworkCount);
        var bootstrap = resources.Single(resource => resource.Name == MongoNativeReadinessRegressionProtocol.Bootstrap);
        var mount = bootstrap.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == MongoNativeReadinessRegressionProtocol.ModuleTarget);
        MongoNativeReadinessRegressionProtocol.Require(mount.Type == ContainerMountType.BindMount && mount.IsReadOnly
            && mount.Source is not null && File.Exists(mount.Source));
        return (image!, common.Single(), Path.GetFullPath(mount.Source!));
    }

    private static async Task<MongoNativeReadinessRegressionIdentity> InspectNodeAsync(ContainerResource node, CancellationToken token)
    {
        var name = node.Annotations.OfType<ContainerNameAnnotation>().Single().Name;
        var annotation = node.Annotations.OfType<ContainerImageAnnotation>().Single();
        MongoNativeReadinessRegressionProtocol.Require(MongoNativeReadinessRegressionProtocol.Identifier(name)
            && name.StartsWith(MongoNativeReadinessRegressionProtocol.Prefix, StringComparison.Ordinal)
            && annotation.SHA256 == BenchmarkResources.MongoDigest[MongoNativeReadinessRegressionProtocol.DigestPrefixLength..]
            && node.TryGetContainerImageName(out _));
        _ = node.TryGetContainerImageName(out var image);
        var actual = await MongoNativeReadinessRegressionDocker.InspectAsync(name, token);
        actual.RequireNode(name, image!, node.Name);
        return actual;
    }
}
