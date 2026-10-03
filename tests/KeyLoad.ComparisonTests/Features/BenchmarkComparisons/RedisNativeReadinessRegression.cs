using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class RedisNativeReadinessRegression
{
    private const string Primary = "primary";
    private const string ProbePrefix = "keyload-native-readiness:";
    private const string GuidFormat = "N";
    private const int MinimumNodes = 1, MaximumNodes = 3;
    private static readonly string[] NodeNames = [Primary, "replica1", "replica2"];
    private static readonly TimeSpan ProbeExpiry = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CancellationDelay = TimeSpan.FromSeconds(1);

    /// <summary>AC-ISO-002/003/006: supplement the container's strict proof with real external copy and cancellation operations.</summary>
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, MinimumNodes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeCount, MaximumNodes);
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<RedisResource>().ToArray();
        await Assert.That(resources.Select(resource => resource.Name)).IsEquivalentTo(NodeNames.Take(nodeCount));
        await using var clients = new RedisNativeReadinessRegressionConnections();
        await clients.ConnectAsync(app, resources, token);
        var database = clients.Primary.GetDatabase();
        var probe = ProbePrefix + Guid.NewGuid().ToString(GuidFormat);
        var payload = Guid.NewGuid().ToString(GuidFormat);
        var created = false;
        try
        {
            created = await database.StringSetAsync(probe, payload, ProbeExpiry, when: When.NotExists, flags: CommandFlags.DemandMaster);
            token.ThrowIfCancellationRequested();
            await Assert.That(created).IsTrue();
            await VerifyCopiesAsync(clients, database, probe, payload, token);
            if (nodeCount > MinimumNodes)
            {
                await VerifyAbsentProbeCancellationAsync(clients, database, token);
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

    private static async Task VerifyCopiesAsync(RedisNativeReadinessRegressionConnections clients, IDatabase database,
        string probe, string payload, CancellationToken token)
    {
        await RedisCopyObservation.VerifyDirectCopiesAsync(clients.Replicas, clients.ReplicaEndpoints,
            database.Database, probe, payload, token);
        var stored = await database.StringGetAsync(probe, CommandFlags.DemandMaster);
        token.ThrowIfCancellationRequested();
        await Assert.That(stored.ToString()).IsEqualTo(payload);
    }

    private static async Task VerifyAbsentProbeCancellationAsync(RedisNativeReadinessRegressionConnections clients,
        IDatabase database, CancellationToken token)
    {
        var absent = ProbePrefix + Guid.NewGuid().ToString(GuidFormat);
        var payload = Guid.NewGuid().ToString(GuidFormat);
        await Assert.That(await database.KeyExistsAsync(absent, CommandFlags.DemandMaster)).IsFalse();
        token.ThrowIfCancellationRequested();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        cancellation.CancelAfter(CancellationDelay);
        var original = RedisCopyObservation.VerifyDirectCopiesAsync(clients.Replicas, clients.ReplicaEndpoints,
            database.Database, absent, payload, cancellation.Token);
        await Assert.That(async () => await original).Throws<OperationCanceledException>();
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(original.IsCanceled).IsTrue();
        token.ThrowIfCancellationRequested();
        await Assert.That(await database.KeyExistsAsync(absent, CommandFlags.DemandMaster)).IsFalse();
    }
}
