using System.Globalization;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class ClusterPublishedPortPolicyTests
{
    private const string ContainerUserSetting = "KeyLoad:ContainerUser";
    private const string ImageReference =
        "ghcr.io/managedcode/keyload:current@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    [Arguments(5301, false)]
    [Arguments(65533, false)]
    [Arguments(1, false)]
    [Arguments(5301, true)]
    public async Task CentralSnapshotControlsEveryActualRf3Endpoint(int firstPort, bool ephemeral)
    {
        var builder = CreateBuilder();
        builder.Configuration[ClusterDeploymentOptions.FirstPublicPortKey] = firstPort.ToString(CultureInfo.InvariantCulture);
        var runtime = AppHostOptionsRegistration.Get(builder);
        builder.Configuration[ClusterDeploymentOptions.FirstPublicPortKey] = "5401";
        var root = NewRoot();
        try
        {
            var nodes = ClusterResources.Add(builder, CreateProfile(), root, ephemeral);
            await Assert.That(nodes.Length).IsEqualTo(3);
            for (var index = 0; index < nodes.Length; index++)
            {
                var http = nodes[index].Resource.Annotations.OfType<EndpointAnnotation>()
                    .Single(endpoint => endpoint.Name == "http");
                // Aspire's proxy-less annotation exposes TargetPort until dynamic host allocation.
                await Assert.That(http.Port).IsEqualTo(ephemeral ? 8080 : firstPort + index);
                await Assert.That(http.TargetPort).IsEqualTo(8080);
                await Assert.That(http.IsProxied).IsFalse();
                await Assert.That(Directory.Exists(Path.Combine(root, nodes[index].Resource.Name))).IsTrue();
            }
            await Assert.That(runtime.Cluster.Value.FirstPublicPort).IsEqualTo(firstPort);
            await Assert.That(ReferenceEquals(runtime, AppHostOptionsRegistration.Get(builder))).IsTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(65534)]
    [Arguments(65535)]
    [Arguments(int.MaxValue)]
    public async Task InvalidFullRf3RangeRejectsBeforeFilesOrResources(int firstPort)
    {
        var builder = CreateBuilder();
        builder.Configuration[ClusterDeploymentOptions.FirstPublicPortKey] = firstPort.ToString(CultureInfo.InvariantCulture);
        var root = NewRoot();
        var failure = Assert.ThrowsExactly<OptionsValidationException>(
            () => ClusterResources.Add(builder, CreateProfile(), root, ephemeral: false));
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ClusterDeploymentOptions));
        await Assert.That(builder.Resources.Count).IsEqualTo(0);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task UnconfiguredCentralPolicyPreservesTheExistingPublishedPort()
    {
        var builder = CreateBuilder();
        await Assert.That(AppHostOptionsRegistration.Get(builder).Cluster.Value.FirstPublicPort).IsEqualTo(5101);
    }

    private static IDistributedApplicationBuilder CreateBuilder()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        { DisableDashboard = true, Args = [] });
        builder.Configuration[ContainerImageOptions.ServerKey] = ImageReference;
        builder.Configuration[ContainerUserSetting] = "1000:1000";
        return builder;
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "keyload-published-ports-" + Guid.NewGuid().ToString("N"));

    private static LocalProfile CreateProfile() => new(ClusterProfileStore.CurrentVersion,
        Guid.NewGuid(), Guid.NewGuid(), Secret(), Secret(), "root." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
