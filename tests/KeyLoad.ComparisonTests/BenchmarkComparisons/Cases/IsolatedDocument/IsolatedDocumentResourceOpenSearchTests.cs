using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using T = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.IsolatedDocumentResourceTokens;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedDocumentResourceOpenSearchTests
{
    private const string Target = "OpenSearch";
    private const string Prefix = "isolated-opensearch-";
    private const string Seeds = "discovery.seed_hosts";
    private const string Managers = "cluster.initial_cluster_manager_nodes";

    /// <summary>AC-ISO-002/003/006: genuine native discovery, official entrypoint and fresh private volumes.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task SelectedGroupUsesExactNativeDiscoveryAndExplicitUnauthenticatedProfile(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, count);
        IsolatedOpenSearchResources.Add(fixture.Context);
        var resources = fixture.Build();
        var nodes = resources.Where(node => node.Name != IsolatedResourceTopologyFixture.RunnerName).ToArray();
        await IsolatedDocumentResourceAssertions.VerifyNodesAsync(nodes, count, Prefix, BenchmarkResources.OpenSearchDigest);
        await IsolatedDocumentResourceAssertions.VerifyVolumesAsync(nodes, T.OpenSearchData);
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(fixture.Context.Runner.Resource, nodes);
        await IsolatedDocumentResourceAssertions.VerifyEndpointsAsync(fixture.Context.Runner.Resource, nodes, T.Http);
        var names = string.Join(',', nodes.Select(node => node.Name));
        foreach (var node in nodes)
        {
            var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
            await Assert.That(environment[T.NodeName]).IsEqualTo(node.Name);
            await Assert.That(environment[T.HeapSetting]).IsEqualTo(T.Heap);
            await Assert.That(environment[T.DisableDemo]).IsEqualTo(T.Enabled);
            await Assert.That(environment[T.DisableSecurity]).IsEqualTo(T.Enabled);
            await Assert.That(environment.GetValueOrDefault(Seeds)).IsEqualTo(count == 1 ? null : names);
            await Assert.That(environment.GetValueOrDefault(Managers)).IsEqualTo(count == 1 ? null : names);
            await Assert.That(environment.GetValueOrDefault(T.DiscoveryType)).IsEqualTo(count == 1 ? T.Single : null);
            await Assert.That(node.Entrypoint).IsNull();
            await Assert.That(node.Annotations.OfType<WaitAnnotation>()).IsEmpty();
            await Assert.That(node.Annotations.OfType<HealthCheckAnnotation>().Count()).IsEqualTo(1);
        }
        var runner = await IsolatedResourceTopologyFixture.EnvironmentAsync(fixture.Context.Runner.Resource);
        await Assert.That(runner[IsolatedDocumentResourceAssertions.Image]).IsEqualTo(
            "opensearchproject/opensearch:3.6.0@" + BenchmarkResources.OpenSearchDigest);
        await Assert.That(runner.ContainsKey(IsolatedDocumentResourceAssertions.NativePrefix + T.NativePassword)).IsFalse();
    }
}
