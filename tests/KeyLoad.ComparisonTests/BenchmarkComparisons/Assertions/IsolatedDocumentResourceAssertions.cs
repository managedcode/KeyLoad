using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedDocumentResourceAssertions
{
    internal const string NativePrefix = "Benchmarks__Native__";
    internal const string Endpoints = NativePrefix + "Endpoints__";
    internal const string Image = NativePrefix + "Image";
    internal const string Connection = NativePrefix + "ConnectionString";
    private const string NativeDirectory = "native";
    private const string MissingSecret = "IsolatedDocumentResourceSecretMissing";
    private const string VolumePrefix = "keyload-isolated-";

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

    internal static async Task VerifyDataAsync(ContainerResource node, string target, string root)
    {
        var mount = node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == target);
        await Assert.That(mount.Type).IsEqualTo(ContainerMountType.BindMount);
        await Assert.That(mount.IsReadOnly).IsFalse();
        await Assert.That(mount.Source).IsEqualTo(Path.Combine(root, NativeDirectory, node.Name));
        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(File.GetUnixFileMode(mount.Source!)).IsEqualTo(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    internal static async Task VerifyVolumesAsync(ContainerResource[] nodes, string target)
    {
        var mounts = nodes.Select(node => node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == target)).ToArray();
        await Assert.That(mounts.All(mount => mount.Type == ContainerMountType.Volume && !mount.IsReadOnly)).IsTrue();
        await Assert.That(mounts.Select(mount => mount.Source).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(nodes.Length);
        await Assert.That(mounts.All(mount => !string.IsNullOrWhiteSpace(mount.Source))).IsTrue();
        await Assert.That(mounts.All(mount => mount.Source!.StartsWith(VolumePrefix, StringComparison.Ordinal))).IsTrue();
    }

    internal static async Task VerifyEndpointsAsync(ContainerResource runner, ContainerResource[] nodes, string endpoint)
    {
        var configuration = await IsolatedResourceTopologyFixture.ConfigurationAsync(runner);
        var endpoints = configuration.EnvironmentVariablesWithUnprocessed.Where(pair => pair.Key.StartsWith(Endpoints, StringComparison.Ordinal)).ToDictionary();
        await Assert.That(endpoints.Count).IsEqualTo(nodes.Length);
        for (var index = 0; index < nodes.Length; index++)
        {
            var value = endpoints[Endpoints + index].Unprocessed;
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
