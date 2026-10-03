using System.Net.Http.Headers;
using System.Text;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Owns only one native target and tracks partial HTTP construction until ownership transfers.</summary>
internal sealed class IsolatedHostTargetOwner : IAsyncDisposable
{
    private readonly List<HttpClient> unownedClients = [];
    private IComparisonTarget? target;

    internal IComparisonTarget Create(IsolatedHostSettings settings)
    {
        if (target is not null)
        {
            throw new InvalidOperationException(IsolatedHostConstants.Failure);
        }
        var native = settings.Native ?? throw new InvalidOperationException(IsolatedHostConstants.Failure);
        var topology = settings.Selection.Options.Topology;
        target = settings.Selection.Target switch
        {
            IsolatedHostConstants.KeyLoad => CreateKeyLoad(settings, native),
            IsolatedHostConstants.Postgres => new PostgresTarget(native.Connection!, settings.RunId, native.Image, topology),
            IsolatedHostConstants.Qdrant => CreateQdrant(settings, native),
            IsolatedHostConstants.Rabbit => new RabbitTarget(native.Connection!, settings.RunId, native.Image, topology, CreateClient(native, 0)),
            IsolatedHostConstants.Redis => new RedisTarget(native.Connection!, settings.RunId, native.Image, topology, [.. native.Replicas]),
            IsolatedHostConstants.Neo4j => new Neo4jTarget(CreateClient(native, 0), settings.RunId, native.Image),
            IsolatedHostConstants.Mongo => new MongoTarget(native.Connection!, settings.RunId, native.Image, topology),
            IsolatedHostConstants.OpenSearch => new OpenSearchTarget(CreateClient(native, 0), settings.RunId, native.Image, topology),
            IsolatedHostConstants.Kurrent => new KurrentTarget(native.Connection!, CreateClients(native), settings.RunId, native.Image, topology),
            _ => throw new InvalidOperationException(IsolatedHostConstants.Failure)
        };
        unownedClients.Clear();
        return target;
    }

    private KeyLoadTarget CreateKeyLoad(IsolatedHostSettings settings, IsolatedHostNativeSettings native)
    {
        var clients = CreateClients(native);
        return new(clients[0], native.AdminKey!, settings.RunId, native.Image, clients, settings.Selection.NodeCount)
        { RequireIsolatedAdmission = true };
    }

    private QdrantTarget CreateQdrant(IsolatedHostSettings settings, IsolatedHostNativeSettings native)
    {
        var clients = CreateClients(native);
        return new(clients[0], settings.RunId, native.Image, settings.Selection.Options.Topology, clients);
    }

    private HttpClient[] CreateClients(IsolatedHostNativeSettings native)
    {
        var clients = new HttpClient[native.Endpoints.Length];
        for (var index = 0; index < clients.Length; index++)
        {
            clients[index] = CreateClient(native, index);
        }
        return clients;
    }

    private HttpClient CreateClient(IsolatedHostNativeSettings native, int index)
    {
        var client = new HttpClient { BaseAddress = native.Endpoints[index], Timeout = Timeout.InfiniteTimeSpan };
        unownedClients.Add(client);
        if (native.User is not null)
        {
            var credential = native.User + IsolatedHostConstants.CredentialSeparator + native.Password;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(IsolatedHostConstants.Basic,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(credential)));
        }
        if (native.ApiKey is not null)
        {
            client.DefaultRequestHeaders.Add(ComparisonHostConstants.QdrantApiKeyHeader, native.ApiKey);
        }
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        var ownedTarget = target;
        target = null;
        try
        {
            if (ownedTarget is not null)
            {
                await DisposeTargetAsync(ownedTarget);
            }
        }
        finally
        {
            foreach (var client in unownedClients)
            {
                client.Dispose();
            }
            unownedClients.Clear();
        }
    }

    private static async Task DisposeTargetAsync(IComparisonTarget ownedTarget)
    {
        var cleanup = ownedTarget.DisposeAsync().AsTask();
        try
        {
            await cleanup.WaitAsync(TimeSpan.FromSeconds(IsolatedHostConstants.CleanupTimeoutSeconds));
        }
        catch (Exception)
        {
            _ = cleanup.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw new ComparisonFailureException(IsolatedHostConstants.CleanupFailure);
        }
    }
}
