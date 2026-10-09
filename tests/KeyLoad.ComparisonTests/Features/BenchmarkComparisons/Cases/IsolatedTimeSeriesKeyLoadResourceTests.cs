using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedTimeSeriesKeyLoadResourceTests
{
    private const string ImageSetting = "KeyLoad:ContainerImages:Server";
    private const string UserSetting = "KeyLoad:ContainerUser";
    private const string ExpectedUser = "1001:1001";
    private const string ExpectedImage = "ghcr.io/managedcode/keyload-server:model@sha256:"
        + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ExpectedDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BenchmarkSetting = "KeyLoad__BenchmarkTopology";
    private const string PeerPrefix = "KeyLoad__Peers__";
    private const string NativePrefix = "Benchmarks__Native__";
    private const string IncarnationParameter = "incarnation";
    private const string NodeIncarnationKey = "KeyLoad__Incarnation";
    private const string NativeIncarnationKey = NativePrefix + "Incarnation";
    private const string NativeVoterIdsPrefix = NativePrefix + "VoterIds__";
    private const string AdminSetting = "Benchmarks__AdminKey";
    private const string NodeAdminSetting = "KeyLoad__AdminKey";
    private const string AdminExpression = "{admin-key.value}";
    private const string NodePrefix = "node";
    private const string DataTarget = "/data";
    private const string Endpoint = "http";
    private const int FirstNodeIndex = 0;
    private static readonly string[] ExpectedOrigins = ["http://node1:8080", "http://node2:8080", "http://node3:8080"];

    /// <summary>AC-TSI-001/006: the new route retains exactly the selected real fixed-voter composition.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task NewTimeSeriesCompositionRetainsExactVotersPrivateStoresAndPersistedAuthority(int count)
    {
        await using var model = new IsolatedTimeSeriesTimescaleResourceModel();
        model.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ImageSetting] = ExpectedImage,
            [UserSetting] = ExpectedUser
        });
        IsolatedTimeSeriesKeyLoadResources.Add(model.Context(count));
        var resources = model.Build();
        var nodes = resources.Where(resource => resource.Name.StartsWith(NodePrefix, StringComparison.Ordinal)).ToArray();
        await Assert.That(resources.Length).IsEqualTo(count + 1);
        await Assert.That(nodes.Select(node => node.Name)).IsEquivalentTo(Enumerable.Range(1, count).Select(NodeName));
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(model.Runner.Resource, nodes);
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(model.Runner.Resource);
        await Assert.That(environment[NativePrefix + "Image"]).IsEqualTo(ExpectedImage);
        await Assert.That(environment[AdminSetting]).IsEqualTo(AdminExpression);
        await Assert.That(environment.Keys.Count(key => key.StartsWith(NativePrefix + "Endpoints__", StringComparison.Ordinal)))
            .IsEqualTo(count);
        foreach (var node in nodes)
        {
            await VerifyNodeAsync(node, model.Root, count);
        }
        for (var index = 0; index < count; index++)
        {
            await Assert.That(environment[NativePrefix + "Endpoints__" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)])
                .IsEqualTo("{" + NodeName(index + 1) + ".bindings." + Endpoint + ".url}");
        }
        await VerifyNativeIdentityBindingsAsync(model.Builder, model.Runner.Resource, nodes, count);
        await Assert.That(nodes.Select(node => node.Annotations.OfType<ContainerNameAnnotation>().Single().Name)
            .Distinct(StringComparer.Ordinal).Count()).IsEqualTo(count);
    }

    private static async Task VerifyNativeIdentityBindingsAsync(IDistributedApplicationBuilder builder,
        ContainerResource runner, ContainerResource[] nodes, int count)
    {
        var incarnation = builder.Resources.OfType<ParameterResource>().Single(parameter => parameter.Name == IncarnationParameter);
        var runnerConfiguration = await IsolatedResourceTopologyFixture.ConfigurationAsync(runner);
        var runnerEnvironment = runnerConfiguration.EnvironmentVariables.ToDictionary();
        var runnerBindings = runnerConfiguration.EnvironmentVariablesWithUnprocessed
            .ToDictionary(pair => pair.Key, pair => pair.Value.Unprocessed);
        await Assert.That(ReferenceEquals(runnerBindings[NativeIncarnationKey], incarnation)).IsTrue();
        await Assert.That(runnerEnvironment.Keys.Count(key => key.StartsWith(NativeVoterIdsPrefix, StringComparison.Ordinal)))
            .IsEqualTo(count);
        for (var index = FirstNodeIndex; index < count; index++)
        {
            var suffix = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var voterKey = NativeVoterIdsPrefix + suffix;
            var peerKey = PeerPrefix + suffix;
            var nodeConfiguration = await IsolatedResourceTopologyFixture.ConfigurationAsync(nodes[index]);
            var nodeEnvironment = nodeConfiguration.EnvironmentVariables.ToDictionary();
            var nodeBindings = nodeConfiguration.EnvironmentVariablesWithUnprocessed
                .ToDictionary(pair => pair.Key, pair => pair.Value.Unprocessed);
            await Assert.That(ReferenceEquals(nodeBindings[NodeIncarnationKey], incarnation)).IsTrue();
            await Assert.That(runnerEnvironment[voterKey]).IsEqualTo(ExpectedOrigins[index]);
            await Assert.That(nodeEnvironment[peerKey]).IsEqualTo(ExpectedOrigins[index]);
            await Assert.That(runnerEnvironment[voterKey]).IsEqualTo(nodeEnvironment[peerKey]);
        }
    }

    private static async Task VerifyNodeAsync(ContainerResource node, string root, int count)
    {
        var mount = node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == DataTarget);
        await Assert.That(mount.Source).IsEqualTo(Path.Combine(root, "native", "keyload", node.Name));
        await Assert.That(mount.IsReadOnly).IsFalse();
        await Assert.That(Directory.Exists(mount.Source)).IsTrue();
        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(File.GetUnixFileMode(mount.Source!)).IsEqualTo(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        await IsolatedResourceTopologyFixture.VerifyUserAsync(node);
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
        await Assert.That(environment[BenchmarkSetting]).IsEqualTo("true");
        await Assert.That(environment[NodeAdminSetting]).IsEqualTo(AdminExpression);
        await Assert.That(environment.Keys.Count(key => key.StartsWith(PeerPrefix, StringComparison.Ordinal))).IsEqualTo(count);
        for (var index = 0; index < count; index++)
        {
            await Assert.That(environment[PeerPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture)])
                .IsEqualTo("http://" + NodeName(index + 1) + ":8080");
        }
        await Assert.That(node.Annotations.OfType<ContainerImageAnnotation>().Single().SHA256).IsEqualTo(ExpectedDigest);
        await Assert.That(node.Annotations.OfType<ContainerNetworkAliasAnnotation>().Single().Alias).IsEqualTo(node.Name);
    }

    private static string NodeName(int index) => NodePrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
