using System.Globalization;
using Aspire.Hosting.ApplicationModel;
using T = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.IsolatedQuorumResourceTokens;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedQuorumResourceAssertions
{
    internal static async Task VerifyGroupAsync(IsolatedQuorumResourceModel model, ContainerResource[] nodes,
        int count, string prefix, string storage, string endpoint, string digest)
    {
        await Assert.That(nodes.Length).IsEqualTo(count);
        var runner = await IsolatedQuorumResourceModel.ReadAsync(model.Runner.Resource);
        var environment = runner.EnvironmentVariablesWithUnprocessed.ToDictionary(pair => pair.Key, pair => pair.Value.Unprocessed);
        await Assert.That(environment.Keys.Count(key => key.StartsWith(T.Endpoints, StringComparison.Ordinal))).IsEqualTo(count);
        var waits = model.Runner.Resource.Annotations.OfType<WaitAnnotation>().ToArray();
        await Assert.That(waits.Length).IsEqualTo(count);
        for (var index = 0; index < count; index++)
        {
            await VerifyNodeAsync(model, nodes[index], prefix, storage, digest, index);
            var reference = (EndpointReference)environment[T.Endpoints + index.ToString(CultureInfo.InvariantCulture)];
            await Assert.That(ReferenceEquals(reference.Resource, nodes[index])).IsTrue();
            await Assert.That(reference.EndpointName).IsEqualTo(endpoint);
            await Assert.That(waits.Any(wait => ReferenceEquals(wait.Resource, nodes[index]))).IsTrue();
        }
    }

    private static async Task VerifyNodeAsync(IsolatedQuorumResourceModel model, ContainerResource node,
        string prefix, string storage, string digest, int index)
    {
        var name = prefix + (index + 1).ToString(CultureInfo.InvariantCulture);
        await Assert.That(node.Name).IsEqualTo(name);
        await Assert.That(node.Annotations.OfType<ContainerNetworkAliasAnnotation>().Select(alias => alias.Alias))
            .IsEquivalentTo(new[] { name });
        var mount = node.Annotations.OfType<ContainerMountAnnotation>().Single(mount => mount.Target == storage);
        await Assert.That(mount.Source).IsEqualTo(Path.Combine(model.Root, T.NativeDirectory, name));
        await Assert.That(mount.Type).IsEqualTo(ContainerMountType.BindMount);
        await Assert.That(mount.IsReadOnly).IsFalse();
        await Assert.That(Directory.Exists(mount.Source)).IsTrue();
        var image = node.Annotations.OfType<ContainerImageAnnotation>().Single();
        await Assert.That(image.SHA256).IsEqualTo(digest[7..]);
        await Assert.That(image.Tag).IsNull();
        await Assert.That(node.Annotations.OfType<EndpointAnnotation>().Any(endpoint => endpoint.TargetPort is 6335 or 4369 or 25672)).IsFalse();
    }

    internal static async Task<ParameterResource> VerifySecretAsync(object value)
    {
        await Assert.That(value).IsTypeOf<ParameterResource>();
        var parameter = (ParameterResource)value;
        await Assert.That(parameter.Secret).IsTrue();
        var secret = await parameter.GetValueAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(secret!.Length).IsEqualTo(64);
        await Assert.That(secret.All(char.IsAsciiHexDigit)).IsTrue();
        return parameter;
    }
}
