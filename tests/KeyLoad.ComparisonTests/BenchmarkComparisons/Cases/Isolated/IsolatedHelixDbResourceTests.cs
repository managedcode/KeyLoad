using Aspire.Hosting.ApplicationModel;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedHelixDbResourceTests
{
    private const string HelixDataDirectorySetting = "HELIX_DATA_DIR";
    [Test]
    public async Task HelixDbUsesPinnedServerWithDirectoryPersistence()
    {
        await using var fixture = new IsolatedResourceTopologyFixture("HelixDB", 1);
        IsolatedHelixDbResources.Add(fixture.Context);
        var resources = fixture.Build();
        await Assert.That(resources.Length).IsEqualTo(1);
        var node = resources.Single(item => item.Name == "isolated-helixdb");
        var runner = fixture.Context.Client;
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(runner, [node]);
        var image = node.Annotations.OfType<ContainerImageAnnotation>().Single();
        await Assert.That(image.Registry).IsNull();
        await Assert.That(image.Image).IsEqualTo("ghcr.io/helixdb/helixdb");
        await Assert.That(image.Tag).IsNull();
        await Assert.That(image.SHA256).IsEqualTo(BenchmarkResources.HelixDbDigest[7..]);
        var mount = node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == "/var/lib/helix");
        await Assert.That(mount.Source).IsNull();
        await Assert.That(mount.Type).IsEqualTo(ContainerMountType.Volume);
        await Assert.That(mount.IsReadOnly).IsFalse();
        await Assert.That(node.Annotations.OfType<EndpointAnnotation>().Single(item => item.Name == "http").TargetPort).IsEqualTo(8080);
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
        await Assert.That(environment[HelixDataDirectorySetting]).IsEqualTo("/var/lib/helix");
        var runnerEnvironment = await IsolatedResourceTopologyFixture.EnvironmentAsync(runner);
        await Assert.That(runnerEnvironment[IsolatedResourceTopologyFixture.RuntimePrefix + "Endpoints__0"])
            .IsEqualTo("{isolated-helixdb.bindings.http.url}");
        await Assert.That(runnerEnvironment[IsolatedResourceTopologyFixture.RuntimePrefix + "Image"])
            .IsEqualTo("ghcr.io/helixdb/helixdb:v0.0.10@" + BenchmarkResources.HelixDbDigest);
    }

    [Test]
    [Arguments(3)]
    public async Task HelixLocalImageDoesNotInventClusterMembers(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture("HelixDB", count);
        await Assert.That(() => IsolatedHelixDbResources.Add(fixture.Context)).Throws<InvalidOperationException>();
        await Assert.That(fixture.Build().Length).IsEqualTo(0);
        await Assert.That(Directory.Exists(Path.Combine(fixture.Context.Root, "execution"))).IsFalse();
    }
}
