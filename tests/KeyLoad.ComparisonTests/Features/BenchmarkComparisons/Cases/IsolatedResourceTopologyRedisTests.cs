using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedResourceTopologyRedisTests
{
    private const string Target = "Redis";
    private const string Primary = "primary";
    private const string NativePrimaryHost = "primary.dev.internal";
    private const string SecretEnvironment = "KEYLOAD_REDIS_PASSWORD";
    private const string PrimaryEnvironment = "KEYLOAD_REDIS_PRIMARY";
    private const string Bootstrap = "/bootstrap/isolated-redis.sh";
    private const string ReplicaPrefix = "Benchmarks__Native__ReplicaConnections__";
    private const string NativeScheme = "redis";
    private const int NativePort = 6379;

    /// <summary>AC-ISO-003/005 and AC-BC-FAIL-009: native authenticated TCP resources with explicit certificate opt-out.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task SelectedRedisHasExactAuthenticatedNativeGroupWithoutSecretArguments(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, count);
        IsolatedRedisResources.Add(fixture.Context);
        var resources = fixture.Build();
        var runner = resources.Single(item => item.Name == IsolatedResourceTopologyFixture.RunnerName);
        var nodes = resources.OfType<RedisResource>().ToArray();
        await Assert.That(resources.Length).IsEqualTo(count + 1);
        await Assert.That(nodes.Length).IsEqualTo(count);
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(runner, nodes);
        var primary = nodes.Single(item => item.Name == Primary);
        await Assert.That(primary.PasswordParameter!.Secret).IsTrue();
        var runnerEnvironment = await IsolatedResourceTopologyFixture.EnvironmentAsync(runner);
        await Assert.That(runnerEnvironment[IsolatedResourceTopologyFixture.NativePrefix + "ConnectionString"])
            .IsEqualTo(primary.ConnectionStringExpression.ValueExpression);
        await Assert.That(runnerEnvironment.Count(item => item.Key.StartsWith(ReplicaPrefix, StringComparison.Ordinal)))
            .IsEqualTo(count - 1);
        await Assert.That(runnerEnvironment.Keys.Count(key => key.StartsWith(
            IsolatedResourceTopologyFixture.NativePrefix + "Endpoints__", StringComparison.Ordinal))).IsEqualTo(count);
        foreach (var node in nodes)
        {
            await VerifyNodeAsync(fixture, node, primary);
        }
        for (var index = 0; index < count; index++)
        {
            var node = index == 0 ? primary : nodes.Single(item => item.Name == "replica" + index);
            await Assert.That(runnerEnvironment[IsolatedResourceTopologyFixture.NativePrefix + "Endpoints__" + index])
                .IsEqualTo("{" + node.Name + ".bindings.tcp.url}");
        }
        for (var index = 0; index < count - 1; index++)
        {
            var replica = nodes.Single(item => item.Name == "replica" + (index + 1));
            await Assert.That(runnerEnvironment[ReplicaPrefix + index]).IsEqualTo(replica.ConnectionStringExpression.ValueExpression);
        }
        await Assert.That(runnerEnvironment[IsolatedResourceTopologyFixture.NativePrefix + "Image"])
            .IsEqualTo("docker.io/library/redis:8.4.0@" + BenchmarkResources.RedisDigest);
    }

    private static async Task VerifyNodeAsync(IsolatedResourceTopologyFixture fixture, RedisResource node, RedisResource primary)
    {
        await VerifyNativeTransportAsync(node);
        await IsolatedResourceTopologyFixture.VerifyPrivateDataAsync(node, fixture.Context.Root);
        await IsolatedResourceTopologyFixture.VerifyUserAsync(node);
        await Assert.That(node.PasswordParameter).IsSameReferenceAs(primary.PasswordParameter);
        var secret = await node.PasswordParameter!.GetValueAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(secret!.Length).IsEqualTo(64);
        await Assert.That(secret.All(char.IsAsciiHexDigit)).IsTrue();
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
        await Assert.That(environment[SecretEnvironment]).IsEqualTo("{" + primary.PasswordParameter!.Name + ".value}");
        await Assert.That(environment.ContainsKey(PrimaryEnvironment)).IsEqualTo(node != primary);
        await Assert.That(environment.GetValueOrDefault(PrimaryEnvironment)).IsEqualTo(node == primary ? null : NativePrimaryHost);
        var waits = node.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name);
        await Assert.That(waits).IsEquivalentTo(node == primary ? Array.Empty<string>() : new[] { Primary });
        var configuration = await IsolatedResourceTopologyFixture.ConfigurationAsync(node);
        await Assert.That(configuration.Arguments.Select(argument => argument.Value)).IsEquivalentTo(new[] { Bootstrap });
        await Assert.That(node.Entrypoint).IsEqualTo("/bin/sh");
        var mount = node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == Bootstrap);
        await Assert.That(mount.IsReadOnly).IsTrue();
        await Assert.That(File.Exists(mount.Source)).IsTrue();
        if (!OperatingSystem.IsWindows())
        {
            await Assert.That((File.GetUnixFileMode(mount.Source!) & UnixFileMode.UserExecute) != 0).IsTrue();
        }
        await Assert.That(node.Annotations.OfType<ContainerNetworkAliasAnnotation>().Single().Alias).IsEqualTo(node.Name);
        await Assert.That(node.Annotations.OfType<ContainerImageAnnotation>().Single().SHA256)
            .IsEqualTo(BenchmarkResources.RedisDigest[7..]);
    }

    private static async Task VerifyNativeTransportAsync(RedisResource node)
    {
        // Inspect the actual pinned Aspire annotation; a missing explicit opt-out must fail this regression.
#pragma warning disable ASPIRECERTIFICATES001
        var certificate = node.Annotations.OfType<HttpsCertificateAnnotation>().Single();
        await Assert.That(certificate.Certificate).IsNull();
        await Assert.That(certificate.UseDeveloperCertificate).IsFalse();
#pragma warning restore ASPIRECERTIFICATES001
        var endpoint = node.Annotations.OfType<EndpointAnnotation>().Single();
        await Assert.That(endpoint.UriScheme).IsEqualTo(NativeScheme);
        await Assert.That(endpoint.TargetPort).IsEqualTo(NativePort);
        await Assert.That(endpoint.TlsEnabled).IsFalse();
        await Assert.That(node.Annotations.OfType<HealthCheckAnnotation>().Any()).IsTrue();
    }
}
