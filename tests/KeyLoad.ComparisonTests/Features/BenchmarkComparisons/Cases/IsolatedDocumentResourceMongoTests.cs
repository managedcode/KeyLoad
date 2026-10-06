using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using T = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.IsolatedDocumentResourceTokens;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedDocumentResourceMongoTests
{
    private const string Target = "MongoDB";
    private const string Prefix = "isolated-mongo-";
    private const string Bootstrap = "isolated-mongo-bootstrap";
    private const string Password = "MONGO_INITDB_ROOT_PASSWORD";
    private const string Key = "KEYLOAD_MONGO_REPLICATION_KEY";
    private const string Script = "/bootstrap/isolated-mongo.sh";
    private const string BootstrapScript = "/bootstrap/isolated-mongo.js";
    private const string ReadinessScript = "/bootstrap/isolated-mongo-readiness.js";
    /// <summary>AC-ISO-002/003/005/006: exact native nodes and a bounded authenticated bootstrap completion barrier.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task NativeGroupUsesSameImageSecretKeyAndBoundedBootstrapBarrier(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, count);
        IsolatedMongoResources.Add(fixture.Context);
        var resources = fixture.Build();
        var nodes = resources.Where(node => node.Name.StartsWith(Prefix, StringComparison.Ordinal) && node.Name != Bootstrap).ToArray();
        await IsolatedDocumentResourceAssertions.VerifyNodesAsync(nodes, count, Prefix, BenchmarkResources.MongoDigest);
        await Assert.That(resources.Length).IsEqualTo(count + 2);
        var password = await IsolatedDocumentResourceAssertions.SecretAsync(nodes[0], Password);
        foreach (var node in nodes)
        {
            await IsolatedDocumentResourceAssertions.VerifyDataAsync(node, T.MongoData, fixture.Context.Root);
            await Assert.That(await IsolatedDocumentResourceAssertions.SecretAsync(node, Password)).IsSameReferenceAs(password);
            await IsolatedDocumentResourceAssertions.VerifyScriptMountAsync(node, Script);
            var configuration = await IsolatedResourceTopologyFixture.ConfigurationAsync(node);
            await Assert.That(configuration.Arguments.Any(argument => argument.Value == T.ReplicaOption)).IsEqualTo(count > 1);
            await Assert.That(node.Annotations.OfType<WaitAnnotation>()).IsEmpty();
        }
        await IsolatedDocumentResourceAssertions.VerifyEndpointsAsync(fixture.Context.Runner.Resource, nodes, T.Tcp);
        await VerifyBootstrapAsync(fixture, resources.Single(node => node.Name == Bootstrap), nodes, password);
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(fixture.Context.Runner.Resource);
        await Assert.That(environment[IsolatedDocumentResourceAssertions.Image]).IsEqualTo(
            "docker.io/library/mongo:8.3.9@" + BenchmarkResources.MongoDigest);
        await Assert.That(environment[IsolatedDocumentResourceAssertions.Connection].Contains("replicaSet=benchmark", StringComparison.Ordinal)).IsEqualTo(count > 1);
        if (count > 1)
        {
            var key = await IsolatedDocumentResourceAssertions.SecretAsync(nodes[0], Key);
            foreach (var node in nodes)
            {
                await Assert.That(await IsolatedDocumentResourceAssertions.SecretAsync(node, Key)).IsSameReferenceAs(key);
            }
        }
        else
        {
            await Assert.That((await IsolatedResourceTopologyFixture.EnvironmentAsync(nodes[0])).ContainsKey(Key)).IsFalse();
        }
    }

    private static async Task VerifyBootstrapAsync(IsolatedResourceTopologyFixture fixture, ContainerResource bootstrap,
        ContainerResource[] nodes, ParameterResource password)
    {
        await Assert.That(await IsolatedDocumentResourceAssertions.SecretAsync(bootstrap, Password)).IsSameReferenceAs(password);
        await Assert.That(bootstrap.Annotations.OfType<ContainerImageAnnotation>().Single().SHA256).IsEqualTo(BenchmarkResources.MongoDigest[7..]);
        await Assert.That(bootstrap.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name)).IsEquivalentTo(nodes.Select(node => node.Name));
        await Assert.That(fixture.Context.Runner.Resource.Annotations.OfType<WaitAnnotation>().Any(wait =>
            ReferenceEquals(wait.Resource, bootstrap) && wait.WaitType == WaitType.WaitForCompletion)).IsTrue();
        await IsolatedDocumentResourceAssertions.VerifyScriptMountAsync(bootstrap, BootstrapScript);
        await IsolatedDocumentResourceAssertions.VerifyScriptMountAsync(bootstrap, ReadinessScript);
        var configuration = await IsolatedResourceTopologyFixture.ConfigurationAsync(bootstrap);
        await Assert.That(configuration.Arguments.Select(argument => argument.Value).SequenceEqual(
            new[] { T.Shell, T.Quiet, T.NoDatabase, BootstrapScript }, StringComparer.Ordinal)).IsTrue();
    }
}
