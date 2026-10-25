using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.FunctionalTests.TestInfrastructure;

internal sealed class ClusterPublishedPortPolicyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AspireAllocatesEveryHttpsAndOrleansEndpoint(bool ephemeral)
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            Args = []
        });
        var root = Path.Combine(AppContext.BaseDirectory, "keyload-published-ports-" + Guid.NewGuid().ToString("N"));
        var profile = new ClusterIdentity(Guid.NewGuid(), Secret(),
            "root." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        try
        {
            var nodes = AppHostProgram.AddCluster(builder, profile, root, ephemeral);
            await Assert.That(nodes.Length).IsEqualTo(3);
            foreach (var node in nodes)
            {
                var endpoints = node.Resource.Annotations.OfType<EndpointAnnotation>().ToArray();
                await Assert.That(endpoints.Select(endpoint => endpoint.Name).ToArray())
                    .IsEquivalentTo(new[] { "https", "silo", "membership" });
                foreach (var endpoint in endpoints)
                {
                    await Assert.That(endpoint.Port).IsNull();
                    await Assert.That(endpoint.TargetPort).IsNull();
                    await Assert.That(endpoint.IsProxied).IsFalse();
                }
                await Assert.That(endpoints.Single(endpoint => endpoint.Name == "https").UriScheme)
                    .IsEqualTo("https");
                await Assert.That(Directory.Exists(Path.Combine(root, node.Resource.Name))).IsTrue();
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
