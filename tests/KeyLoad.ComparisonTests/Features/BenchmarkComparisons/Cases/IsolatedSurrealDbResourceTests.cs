using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedSurrealDbResourceTests
{
    private const string UserSetting = "SURREAL_USER";
    private const string PasswordSetting = "SURREAL_PASS";
    [Test]
    public async Task SurrealDbUsesOnePinnedPersistentServerAndRunnerEndpoint()
    {
        await using var fixture = new IsolatedResourceTopologyFixture("SurrealDB", 1);
        IsolatedSurrealDbResources.Add(fixture.Context);
        var resources = fixture.Build();
        await Assert.That(resources.Length).IsEqualTo(2);
        var node = resources.Single(item => item.Name == "isolated-surrealdb");
        var runner = resources.Single(item => item.Name == IsolatedResourceTopologyFixture.RunnerName);
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(runner, [node]);
        await VerifyNodeAsync(node, fixture.Context.Root);
        var runnerEnvironment = await IsolatedResourceTopologyFixture.EnvironmentAsync(runner);
        await Assert.That(runnerEnvironment[IsolatedResourceTopologyFixture.NativePrefix + "Endpoints__0"])
            .IsEqualTo("{isolated-surrealdb.bindings.http.url}");
        await Assert.That(runnerEnvironment[IsolatedResourceTopologyFixture.NativePrefix + "Image"])
            .IsEqualTo("docker.io/surrealdb/surrealdb:v3.2.4@" + BenchmarkResources.SurrealDbDigest);
        var password = fixture.Context.Builder.Resources.OfType<ParameterResource>().Single();
        await Assert.That(password.Secret).IsTrue();
    }

    [Test]
    [Arguments(3)]
    public async Task SurrealDbDoesNotInventCommunityClusterMembers(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture("SurrealDB", count);
        await Assert.That(() => IsolatedSurrealDbResources.Add(fixture.Context)).Throws<InvalidOperationException>();
        await Assert.That(fixture.Build().Length).IsEqualTo(1);
        await Assert.That(fixture.Context.Builder.Resources.OfType<ParameterResource>().Any()).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(fixture.Context.Root, "native"))).IsFalse();
    }

    internal static async Task VerifyNodeAsync(ContainerResource node, string root)
    {
        var image = node.Annotations.OfType<ContainerImageAnnotation>().Single();
        await Assert.That(image.Registry).IsEqualTo(string.Empty);
        await Assert.That(image.Image).IsEqualTo("surrealdb/surrealdb");
        await Assert.That(image.Tag).IsNull();
        await Assert.That(image.SHA256).IsEqualTo(BenchmarkResources.SurrealDbDigest[7..]);
        var mount = node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == "/data");
        await Assert.That(mount.Source).IsEqualTo(Path.Combine(root, "native", "isolated-surrealdb"));
        await Assert.That(mount.IsReadOnly).IsFalse();
        await Assert.That(node.Annotations.OfType<EndpointAnnotation>().Single(item => item.Name == "http").TargetPort).IsEqualTo(8000);
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
        await Assert.That(environment[UserSetting]).IsEqualTo("root");
        await Assert.That(environment[PasswordSetting]).IsEqualTo("{isolated-surrealdb-password.value}");
        var config = await IsolatedResourceTopologyFixture.ConfigurationAsync(node);
        await Assert.That(config.Arguments.Select(argument => argument.Value)).IsEquivalentTo(new[]
        {
            "start", "--bind", "0.0.0.0:8000", "rocksdb:/data/benchmark.db"
        });
    }
}
