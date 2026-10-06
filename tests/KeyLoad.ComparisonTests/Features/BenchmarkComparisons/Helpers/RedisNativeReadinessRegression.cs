using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class RedisNativeReadinessRegression
{
    private const string Primary = "primary";
    private const string ProbePrefix = "keyload-native-readiness:";
    private const string GuidFormat = "N";
    private const string NativeScheme = "redis";
    private const int MinimumNodes = 1, MaximumNodes = 3;
    private const int NativePort = 6379;
    private static readonly string[] NodeNames = [Primary, "replica1", "replica2"];

    /// <summary>AC-ISO-002/003/006 and AC-BC-FAIL-009: prove native authenticated TCP, external copies and cancellation.</summary>
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, MinimumNodes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeCount, MaximumNodes);
        var policy = NativeExecutionPolicyFixture.Harness().Value;
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<RedisResource>().ToArray();
        await Assert.That(resources.Select(resource => resource.Name)).IsEquivalentTo(NodeNames.Take(nodeCount));
        await VerifyNativeTransportAsync(app, resources, token);
        await using var clients = new RedisNativeReadinessRegressionConnections();
        await clients.ConnectAsync(app, resources, token);
        var database = clients.Primary.GetDatabase();
        await database.PingAsync(CommandFlags.DemandMaster).WaitAsync(token);
        var probe = ProbePrefix + Guid.NewGuid().ToString(GuidFormat);
        var payload = Guid.NewGuid().ToString(GuidFormat);
        var created = false;
        try
        {
            created = await database.StringSetAsync(probe, payload, policy.RedisReadinessProbeExpiry, when: When.NotExists, flags: CommandFlags.DemandMaster);
            token.ThrowIfCancellationRequested();
            await Assert.That(created).IsTrue();
            await VerifyCopiesAsync(clients, database, probe, payload, token);
            if (nodeCount > MinimumNodes)
            {
                await VerifyAbsentProbeCancellationAsync(clients, database, policy.RedisReadinessCancellationDelay, token);
                await VerifyCopiesAsync(clients, database, probe, payload, token);
            }
        }
        finally
        {
            if (created)
            {
                await database.KeyDeleteAsync(probe, CommandFlags.DemandMaster);
            }
        }
    }

    private static async Task VerifyNativeTransportAsync(DistributedApplication app, RedisResource[] resources,
        CancellationToken token)
    {
        foreach (var resource in resources)
        {
            var endpoint = resource.Annotations.OfType<EndpointAnnotation>().Single();
            await Assert.That(endpoint.UriScheme).IsEqualTo(NativeScheme);
            await Assert.That(endpoint.TargetPort).IsEqualTo(NativePort);
            await Assert.That(endpoint.TlsEnabled).IsFalse();
            var connectionString = await app.GetConnectionStringAsync(resource.Name, token);
            await Assert.That(string.IsNullOrWhiteSpace(connectionString)).IsFalse();
            var options = ConfigurationOptions.Parse(connectionString!);
            await Assert.That(options.Ssl).IsFalse();
            await Assert.That(string.IsNullOrWhiteSpace(options.Password)).IsFalse();
        }
    }

    private static async Task VerifyCopiesAsync(RedisNativeReadinessRegressionConnections clients, IDatabase database,
        string probe, string payload, CancellationToken token)
    {
        await RedisCopyObservation.VerifyDirectCopiesAsync(clients.Replicas, clients.ReplicaEndpoints,
            database.Database, probe, payload, NativeExecutionPolicyFixture.Lifecycle(), token);
        var stored = await database.StringGetAsync(probe, CommandFlags.DemandMaster);
        token.ThrowIfCancellationRequested();
        await Assert.That(stored.ToString()).IsEqualTo(payload);
    }

    private static async Task VerifyAbsentProbeCancellationAsync(RedisNativeReadinessRegressionConnections clients,
        IDatabase database, TimeSpan cancellationDelay, CancellationToken token)
    {
        var absent = ProbePrefix + Guid.NewGuid().ToString(GuidFormat);
        var payload = Guid.NewGuid().ToString(GuidFormat);
        await Assert.That(await database.KeyExistsAsync(absent, CommandFlags.DemandMaster)).IsFalse();
        token.ThrowIfCancellationRequested();
        using var cancellationTimeout = new CancellationTokenSource(cancellationDelay, TimeProvider.System);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationTimeout.Token);
        var original = RedisCopyObservation.VerifyDirectCopiesAsync(clients.Replicas, clients.ReplicaEndpoints,
            database.Database, absent, payload, NativeExecutionPolicyFixture.Lifecycle(), cancellation.Token);
        await Assert.That(async () => await original).Throws<OperationCanceledException>();
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(original.IsCanceled).IsTrue();
        token.ThrowIfCancellationRequested();
        await Assert.That(await database.KeyExistsAsync(absent, CommandFlags.DemandMaster)).IsFalse();
    }
}
