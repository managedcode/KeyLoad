using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ClusterTelemetryResourceTests
{
    private const string Development = "Development";
    private const string Production = "Production";
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

    [Test]
    [Arguments(Development, false)]
    [Arguments(Production, false)]
    [Arguments(Development, true)]
    [Arguments(Production, true)]
    public async Task ActualClusterModelKeepsNativeOptionalRoutingAndPrivacyAfterAllCallbacks(
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
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var profile = new LocalProfile(ClusterProfileStore.CurrentVersion, Guid.NewGuid(),
                Secret(), RootCredentialPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)));
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

    private static async Task VerifyNodeAsync(IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> node, string root, bool configuredReceiver)
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
        await Assert.That(environment[DataDirectory]).IsEqualTo(Path.GetFullPath(Path.Combine(root, node.Resource.Name)));
        await Assert.That(node.Resource.Annotations.OfType<EndpointAnnotation>()
            .Single(endpoint => endpoint.Name == HttpEndpoint).IsProxied).IsFalse();
        await Assert.That(node.Resource.GetProjectMetadata().ProjectPath).EndsWith("KeyLoad.csproj");
        var storage = builder.Resources.OfType<AzureStorageResource>().ToArray();
        await Assert.That(storage.Select(resource => resource.Name)).IsEquivalentTo(new[] { "orleans-storage" });
        await Assert.That(builder.Resources.OfType<ContainerResource>()).IsEmpty();
        var emulator = storage.Single();
        await Assert.That(emulator.Annotations.OfType<EmulatorResourceAnnotation>()).HasSingleItem();
        var image = emulator.Annotations.OfType<ContainerImageAnnotation>().Single();
        await Assert.That(image.Registry).IsEqualTo("mcr.microsoft.com");
        await Assert.That(image.Image).IsEqualTo("azure-storage/azurite");
        await Assert.That(string.IsNullOrWhiteSpace(image.Tag)).IsFalse();
        await Assert.That(emulator.Annotations.OfType<ContainerLifetimeAnnotation>().Single().Lifetime)
            .IsEqualTo(ContainerLifetime.Session);
        await Assert.That(Directory.Exists(Path.Combine(root, node.Resource.Name))).IsTrue();
    }

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));
}
