using Aspire.Hosting.ApplicationModel;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedDocumentResourceAssertions
{
    internal const string RuntimePrefix = "Benchmarks__Execution__";
    internal const string Endpoints = RuntimePrefix + "Endpoints__";
    internal const string Image = RuntimePrefix + "Image";
    internal const string Connection = RuntimePrefix + "ConnectionString";
    private const string MissingSecret = "IsolatedDocumentResourceSecretMissing";

    internal static async Task VerifyNodesAsync(ContainerResource[] nodes, int count, string prefix, string digest)
    {
        await Assert.That(nodes.Length).IsEqualTo(count);
        for (var index = 0; index < count; index++)
        {
            var node = nodes[index];
            await Assert.That(node.Name).IsEqualTo(prefix + (index + 1));
            await Assert.That(node.Annotations.OfType<ContainerNetworkAliasAnnotation>().Single().Alias).IsEqualTo(node.Name);
            await Assert.That(node.Annotations.OfType<ContainerImageAnnotation>().Single().SHA256).IsEqualTo(digest[7..]);
            await Assert.That(node.Annotations.OfType<ContainerRuntimeArgsCallbackAnnotation>()).IsEmpty();
        }
    }

    internal static async Task VerifyDataAsync(ContainerResource node, string target)
    {
        var mount = node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == target);
        await Assert.That(mount.Type).IsEqualTo(ContainerMountType.Volume);
        await Assert.That(mount.IsReadOnly).IsFalse();
        await Assert.That(mount.Source).IsNull();
        await Assert.That(node.Annotations.OfType<ContainerLifetimeAnnotation>().Single().Lifetime)
            .IsEqualTo(ContainerLifetime.Session);
    }

    internal static async Task VerifyVolumesAsync(ContainerResource[] nodes, string target)
    {
        var mounts = nodes.Select(node => node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == target)).ToArray();
        await Assert.That(mounts.All(mount => mount.Type == ContainerMountType.Volume && !mount.IsReadOnly)).IsTrue();
        await Assert.That(mounts.Distinct(ReferenceEqualityComparer.Instance).Count()).IsEqualTo(nodes.Length);
        await Assert.That(mounts.All(mount => mount.Source is null)).IsTrue();
        await Assert.That(nodes.All(node => node.Annotations.OfType<ContainerLifetimeAnnotation>().Single().Lifetime
            == ContainerLifetime.Session)).IsTrue();
    }

    internal static async Task VerifyEndpointsAsync(LoadTestClientSettings client, ContainerResource[] nodes, string endpoint)
    {
        var endpoints = client.Values.Where(pair => pair.Key.StartsWith(Endpoints, StringComparison.Ordinal)).ToDictionary();
        await Assert.That(endpoints.Count).IsEqualTo(nodes.Length);
        for (var index = 0; index < nodes.Length; index++)
        {
            var value = endpoints[Endpoints + index];
            await Assert.That(value).IsTypeOf<EndpointReference>();
            var reference = (EndpointReference)value;
            await Assert.That(reference.Resource).IsSameReferenceAs(nodes[index]);
            await Assert.That(reference.EndpointName).IsEqualTo(endpoint);
        }
    }

    internal static async Task<ParameterResource> SecretAsync(ContainerResource node, string environment)
    {
        var configuration = await IsolatedResourceTopologyFixture.ConfigurationAsync(node);
        var value = configuration.EnvironmentVariablesWithUnprocessed.Single(pair => pair.Key == environment).Value.Unprocessed;
        await Assert.That(value).IsTypeOf<ParameterResource>();
        var parameter = (ParameterResource)value;
        await Assert.That(parameter.Secret).IsTrue();
        var secret = await parameter.GetValueAsync(TestContext.Current!.Execution.CancellationToken) ?? throw new InvalidOperationException(MissingSecret);
        await Assert.That(secret.Length).IsEqualTo(64);
        await Assert.That(secret.All(char.IsAsciiHexDigit)).IsTrue();
        await Assert.That(configuration.Arguments.Any(argument => argument.Value.Contains(secret, StringComparison.Ordinal))).IsFalse();
        return parameter;
    }

    internal static async Task VerifyScriptMountAsync(ContainerResource node, string target)
    {
        var mount = node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == target);
        await Assert.That(mount.IsReadOnly).IsTrue();
        await Assert.That(File.Exists(mount.Source)).IsTrue();
    }
}
