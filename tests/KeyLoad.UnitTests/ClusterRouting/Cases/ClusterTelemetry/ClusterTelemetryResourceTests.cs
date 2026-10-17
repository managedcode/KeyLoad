using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ClusterTelemetryResourceTests
{
    private const string Development = "Development";
    private const string Production = "Production";
    private const string ContainerUserKey = "KeyLoad:ContainerUser";
    private const string ContainerUser = "1000:1000";
    private const string RootPrefix = "keyload-otlp-model-";
    private const string GuidFormat = "N";
    private const string RootCredentialPrefix = "root.";
    private const string ServiceName = "OTEL_SERVICE_NAME";
    private const string ResourceAttributes = "OTEL_RESOURCE_ATTRIBUTES";
    private const string DataDirectory = "KeyLoad__DataDirectory";
    private const string HttpEndpoint = "http";
    private const int SecretBytes = 32;
    private const string Endpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string Protocol = "OTEL_EXPORTER_OTLP_PROTOCOL";
    private const string GenAi = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
    private const string Exemplars = "OTEL_METRICS_EXEMPLAR_FILTER";
    private const string ModelReceiver = "http://collector.example.invalid:4317";
    private const string ReceiverConfiguration = "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL";
    private const string LegacyReceiverConfiguration = "DOTNET_DASHBOARD_OTLP_ENDPOINT_URL";
    private const string Image = "ghcr.io/managedcode/keyload:current@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    [Arguments(Development, false)]
    [Arguments(Production, false)]
    [Arguments(Development, true)]
    [Arguments(Production, true)]
    public async Task ActualRf3ModelKeepsNativeOptionalRoutingAndPrivacyAfterAllCallbacks(
        string environmentName, bool configuredReceiver)
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            Args = []
        });
        builder.Environment.EnvironmentName = environmentName;
        builder.Configuration[ReceiverConfiguration] = configuredReceiver ? ModelReceiver : null;
        builder.Configuration[LegacyReceiverConfiguration] = null;
        builder.Configuration[ContainerImageOptions.ServerKey] = Image;
        builder.Configuration[ContainerUserKey] = ContainerUser;
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var profile = new LocalProfile(ClusterProfileStore.CurrentVersion, Guid.NewGuid(), Guid.NewGuid(),
                Secret(), Secret(), RootCredentialPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)));
            var nodes = ClusterResources.Add(builder, profile, root, ephemeral: true);
            await Assert.That(nodes.Select(node => node.Resource.Name).Order(StringComparer.Ordinal).ToArray())
                .IsEquivalentTo(new[] { "node1", "node2", "node3" });
            foreach (var node in nodes)
            {
                await VerifyNodeAsync(builder, node, root, configuredReceiver);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() =>
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            return Task.CompletedTask;
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    [Test]
    [Arguments(Development, false)]
    [Arguments(Production, false)]
    [Arguments(Development, true)]
    [Arguments(Production, true)]
    public async Task ActualTwoRf3ModelKeepsNativeOptionalRoutingAndPrivacyAfterAllCallbacks(
        string environmentName, bool configuredReceiver)
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            Args = []
        });
        builder.Environment.EnvironmentName = environmentName;
        builder.Configuration[ReceiverConfiguration] = configuredReceiver ? ModelReceiver : null;
        builder.Configuration[LegacyReceiverConfiguration] = null;
        builder.Configuration[ContainerImageOptions.ServerKey] = Image;
        builder.Configuration[ContainerUserKey] = ContainerUser;
        builder.Configuration[TwoRf3ProfileProtocol.EphemeralSetting] = TwoRf3ProfileProtocol.Enabled;
        builder.Configuration[TwoRf3ProfileProtocol.Setting] = TwoRf3ProfileProtocol.Profile;
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var profile = new LocalProfile(ClusterProfileStore.CurrentVersion, Guid.NewGuid(), Guid.NewGuid(),
                Secret(), Secret(), RootCredentialPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)));
            var nodes = TwoRf3ClusterResources.Add(builder, profile, root);
            await Assert.That(nodes.Select(node => node.Resource.Name).Order(StringComparer.Ordinal).ToArray())
                .IsEquivalentTo(new[] { "node1", "node2", "node3", "node4", "node5", "node6" });
            foreach (var node in nodes)
            {
                await VerifyNodeAsync(builder, node, root, configuredReceiver);
            }
        }, failures);
        ServerFailureObserver.Observe(() =>
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyNodeAsync(IDistributedApplicationBuilder builder,
        IResourceBuilder<ContainerResource> node, string root, bool configuredReceiver)
    {
        var annotation = node.Resource.Annotations.OfType<OtlpExporterAnnotation>().Single();
        await Assert.That(annotation.RequiredProtocol).IsEqualTo(OtlpProtocol.Grpc);
        var environment = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, node.Resource, environment,
            TestContext.Current!.Execution.CancellationToken);
        foreach (var callback in node.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }
        await Assert.That(environment[GenAi]).IsEqualTo("false");
        await Assert.That(environment[Exemplars]).IsEqualTo("always_off");
        await Assert.That(environment.ContainsKey(Endpoint)).IsEqualTo(configuredReceiver);
        await Assert.That(environment.ContainsKey(Protocol)).IsEqualTo(configuredReceiver);
        if (configuredReceiver)
        {
            await Assert.That(environment[Endpoint] is HostUrl).IsTrue();
            await Assert.That(((HostUrl)environment[Endpoint]).Url).IsEqualTo(ModelReceiver);
            await Assert.That(environment[Protocol]).IsEqualTo("grpc");
            await Assert.That(environment.ContainsKey(ServiceName)).IsTrue();
        }
        else
        {
            await Assert.That(environment.ContainsKey(ServiceName)).IsFalse();
            await Assert.That(environment.ContainsKey(ResourceAttributes)).IsFalse();
        }
        await Assert.That(environment[DataDirectory]).IsEqualTo("/data");
        await Assert.That(node.Resource.Annotations.OfType<EndpointAnnotation>()
            .Single(endpoint => endpoint.Name == HttpEndpoint).TargetPort).IsEqualTo(8080);
        await Assert.That(Directory.Exists(Path.Combine(root, node.Resource.Name))).IsTrue();
    }

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));
}
