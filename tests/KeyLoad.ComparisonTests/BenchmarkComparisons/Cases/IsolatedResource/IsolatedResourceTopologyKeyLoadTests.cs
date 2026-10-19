using Aspire.Hosting.ApplicationModel;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedResourceTopologyKeyLoadTests
{
    private const string Target = "KeyLoad";
    private const string HttpEndpoint = "http";
    private const string SiloCountSetting = "KeyLoad__SiloCount";
    private const string CopyCountSetting = "KeyLoad__ReplicaCount";
    private const string SiloNameSetting = "KeyLoad__SiloName";
    private const string DataDirectorySetting = "KeyLoad__DataDirectory";
    private const string RuntimeImageSetting = IsolatedResourceTopologyFixture.RuntimePrefix + "Image";
    private const string AdminParameter = "admin-key";
    private const string AdminSetting = "Benchmarks__AdminKey";
    private const string RuntimeAdminSetting = "KeyLoad__AdminKey";
    private const string HttpPrefix = "KeyLoad__HttpAdmission__";
    private const string RequestCount = "512";
    private const string ReservedBytes = "2147483648";

    /// <summary>AC-ISO-003/004: real silos share credentials and retain independent ZoneTree directories.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task SelectedKeyLoadBuildsSilosAndDiscoveredClientEndpoints(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, count);
        IsolatedKeyLoadResources.Add(fixture.Context);
        var resources = fixture.Build();
        var runner = fixture.Context.Client;
        var nodes = fixture.Projects;
        await Assert.That(nodes.Length).IsEqualTo(count);
        await Assert.That(resources.Select(resource => resource.Name)).IsEquivalentTo(new[] { "orleans-storage" });
        await Assert.That(nodes.Select(item => item.Name).ToArray())
            .IsEquivalentTo(Enumerable.Range(1, count).Select(index => "node" + index).ToArray());
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(runner, nodes);
        var runnerEnvironment = await IsolatedResourceTopologyFixture.EnvironmentAsync(runner);
        await Assert.That(runnerEnvironment.ContainsKey(RuntimeImageSetting)).IsFalse();
        await Assert.That(runnerEnvironment[AdminSetting]).IsEqualTo("{" + AdminParameter + ".value}");
        foreach (var node in nodes)
        {
            await VerifySiloAsync(fixture, node, count);
        }
        var endpoints = runnerEnvironment.Where(item => item.Key.StartsWith(
            IsolatedResourceTopologyFixture.RuntimePrefix + "Endpoints__", StringComparison.Ordinal)).ToArray();
        await Assert.That(endpoints.Length).IsEqualTo(count);
        for (var index = 0; index < count; index++)
        {
            await Assert.That(endpoints.Single(item => item.Key.EndsWith("__" + index, StringComparison.Ordinal)).Value)
                .IsEqualTo("{node" + (index + 1) + ".bindings." + HttpEndpoint + ".url}");
        }
    }

    private static async Task VerifySiloAsync(IsolatedResourceTopologyFixture fixture, ProjectResource node, int count)
    {
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
        var directory = Path.GetFullPath(Path.Combine(fixture.Context.Root, "execution", "keyload", node.Name));
        await Assert.That(environment[DataDirectorySetting]).IsEqualTo(directory);
        await Assert.That(Directory.Exists(directory)).IsTrue();
        await Assert.That(node.GetProjectMetadata().ProjectPath).EndsWith("KeyLoad.csproj");
        await Assert.That(environment[SiloNameSetting]).IsEqualTo(node.Name);
        await Assert.That(environment[SiloCountSetting]).IsEqualTo(count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(environment[CopyCountSetting]).IsEqualTo(count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(environment[RuntimeAdminSetting]).IsEqualTo("{" + AdminParameter + ".value}");
        await Assert.That(environment[HttpPrefix + nameof(HttpAdmissionLimits.MaxRequests)]).IsEqualTo(RequestCount);
        await Assert.That(environment[HttpPrefix + nameof(HttpAdmissionLimits.MaxTenantRequests)]).IsEqualTo(RequestCount);
        await Assert.That(environment[HttpPrefix + nameof(HttpAdmissionLimits.MaxPrincipalRequests)]).IsEqualTo(RequestCount);
        await Assert.That(environment[HttpPrefix + nameof(HttpAdmissionLimits.ReservedControlRequests)]).IsEqualTo(RequestCount);
        await Assert.That(environment[HttpPrefix + nameof(HttpAdmissionLimits.MaxTenantControlRequests)]).IsEqualTo(RequestCount);
        await Assert.That(environment[HttpPrefix + nameof(HttpAdmissionLimits.MaxPrincipalControlRequests)]).IsEqualTo(RequestCount);
        await Assert.That(environment[HttpPrefix + nameof(HttpAdmissionLimits.MaxReservedBytes)]).IsEqualTo(ReservedBytes);
        await Assert.That(node.Annotations.OfType<EndpointAnnotation>().Single(endpoint => endpoint.Name == HttpEndpoint).IsProxied).IsFalse();
    }
}
