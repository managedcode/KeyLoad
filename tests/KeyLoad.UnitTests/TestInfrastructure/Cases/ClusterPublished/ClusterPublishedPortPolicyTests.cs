using System.Globalization;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class ClusterPublishedPortPolicyTests
{
    [Test]
    [Arguments(5301, false)]
    [Arguments(65533, false)]
    [Arguments(1, false)]
    [Arguments(5301, true)]
    public async Task CentralSnapshotControlsEveryActualClusterEndpoint(int firstPort, bool ephemeral)
    {
        var builder = CreateBuilder();
        builder.Configuration[ClusterDeploymentOptions.FirstPublicPortKey] = firstPort.ToString(CultureInfo.InvariantCulture);
        var runtime = AppHostOptionsRegistration.Get(builder);
        builder.Configuration[ClusterDeploymentOptions.FirstPublicPortKey] = "5401";
        var root = NewRoot();
        try
        {
            var nodes = KeyLoad.AppHost.Hosting.AppHostProgram.AddCluster(builder, CreateProfile(), root, ephemeral);
            await Assert.That(nodes.Length).IsEqualTo(3);
            for (var index = 0; index < nodes.Length; index++)
            {
                var http = nodes[index].Resource.Annotations.OfType<EndpointAnnotation>()
                    .Single(endpoint => endpoint.Name == "http");
                var expected = ephemeral ? (int?)null : firstPort + index;
                await Assert.That(http.Port).IsEqualTo(expected);
                await Assert.That(http.TargetPort).IsEqualTo(expected);
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
    public async Task InvalidFullClusterRangeRejectsBeforeFilesOrResources(int firstPort)
    {
        var builder = CreateBuilder();
        builder.Configuration[ClusterDeploymentOptions.FirstPublicPortKey] = firstPort.ToString(CultureInfo.InvariantCulture);
        var root = NewRoot();
        var failure = Assert.ThrowsExactly<OptionsValidationException>(
            () => KeyLoad.AppHost.Hosting.AppHostProgram.AddCluster(builder, CreateProfile(), root, ephemeral: false));
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
        {
            DisableDashboard = true,
            Args = []
        });
        return builder;
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "keyload-published-ports-" + Guid.NewGuid().ToString("N"));

    private static LocalProfile CreateProfile() => new(ClusterProfileStore.CurrentVersion,
        Guid.NewGuid(), Secret(), "root." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
