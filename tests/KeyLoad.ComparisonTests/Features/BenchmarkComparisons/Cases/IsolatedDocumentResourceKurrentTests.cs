using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using T = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.IsolatedDocumentResourceTokens;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedDocumentResourceKurrentTests
{
    private const string Target = "KurrentDB";
    private const string Prefix = "isolated-kurrent-";
    private const string NativeHostSuffix = ".dev.internal";
    private const string ConnectionPrefix = "esdb://";
    private const string ConnectionOptions = "?tls=false&nodePreference=leader";
    private const string PortSeparator = ":";
    private const int NativeHttpPort = 2113;

    /// <summary>AC-ISO-002/003/005/006: native gossip/replication matches actual direct endpoints for every member.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task SelectedGroupHasExactNativeSizeAndReplicationAdvertisedEndpoints(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, count);
        IsolatedKurrentResources.Add(fixture.Context);
        var resources = fixture.Build();
        var nodes = resources.Where(node => node.Name != IsolatedResourceTopologyFixture.RunnerName).ToArray();
        await IsolatedDocumentResourceAssertions.VerifyNodesAsync(nodes, count, Prefix, BenchmarkResources.KurrentDigest);
        await IsolatedDocumentResourceAssertions.VerifyVolumesAsync(nodes, T.KurrentData);
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(fixture.Context.Runner.Resource, nodes);
        await IsolatedDocumentResourceAssertions.VerifyEndpointsAsync(fixture.Context.Runner.Resource, nodes, T.Http);
        foreach (var node in nodes)
        {
            var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
            await Assert.That(environment[T.ClusterSize]).IsEqualTo(count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await Assert.That(environment[T.NodeIp]).IsEqualTo(T.BindAll);
            await Assert.That(environment[T.ReplicationIp]).IsEqualTo(T.BindAll);
            await Assert.That(environment[T.ReplicationPort]).IsEqualTo(T.TcpPort);
            await Assert.That(environment[T.NodePort]).IsEqualTo(T.HttpPort);
            await Assert.That(environment[T.NodeAdvertise]).IsEqualTo(node.Name + NativeHostSuffix);
            await Assert.That(environment[T.ReplicationAdvertise]).IsEqualTo(node.Name + NativeHostSuffix);
            await Assert.That(node.Annotations.OfType<EndpointAnnotation>().Single().TargetPort).IsEqualTo(NativeHttpPort);
            await Assert.That(environment[T.Insecure]).IsEqualTo(T.Enabled);
            await Assert.That(environment[T.DiscoverDns]).IsEqualTo(T.Disabled);
            await Assert.That(environment.GetValueOrDefault(T.GossipSeeds)).IsEqualTo(count == 1 ? null :
                string.Join(',', nodes.Where(other => other != node)
                    .Select(other => other.Name + NativeHostSuffix + PortSeparator + T.HttpPort)));
            await Assert.That(node.Entrypoint).IsNull();
            await Assert.That(node.Annotations.OfType<WaitAnnotation>()).IsEmpty();
            await Assert.That(node.Annotations.OfType<HealthCheckAnnotation>().Count()).IsEqualTo(1);
        }
        var runner = await IsolatedResourceTopologyFixture.EnvironmentAsync(fixture.Context.Runner.Resource);
        await Assert.That(runner[IsolatedDocumentResourceAssertions.Image]).IsEqualTo(
            "docker.io/kurrentplatform/kurrentdb:26.1.2@" + BenchmarkResources.KurrentDigest);
        await Assert.That(runner[IsolatedDocumentResourceAssertions.Connection].Contains("tls=false", StringComparison.Ordinal)).IsTrue();
        await Assert.That(runner[IsolatedDocumentResourceAssertions.Connection]).IsEqualTo(ConnectionPrefix +
            string.Join(',', nodes.Select(node => node.Name + NativeHostSuffix + PortSeparator + T.HttpPort)) + ConnectionOptions);
    }
}
