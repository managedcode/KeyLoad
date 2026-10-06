using System.Net.Http.Headers;
using System.Text;
using KeyLoad.Client;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Owns only one native target and tracks partial HTTP construction until ownership transfers.</summary>
internal sealed class IsolatedHostTargetOwner(IOptions<NativeComparisonExecutionOptions> executionOptions,
    IOptions<IsolatedKeyLoadAdmissionOptions> admissionOptions,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions, IOptions<KeyLoadClientExecutionOptions> clientOptions, IOptions<QueryTranslationOptions> translationOptions) : IAsyncDisposable
{
    private readonly IOptions<NativeComparisonExecutionOptions> execution = NativeComparisonExecutionOptions.Require(executionOptions);
    private NativeComparisonExecutionOptions Policy => execution.Value;
    internal IOptions<NativeComparisonExecutionOptions> ExecutionOptions => execution;
    private readonly List<HttpClient> unownedClients = [];
    private IComparisonTarget? target;
    private IVectorComparisonTarget? vectorTarget;

    internal IComparisonTarget Create(IsolatedHostSettings settings)
    {
        const int PrimaryEndpointIndex = 0;

        if (target is not null)
        {
            throw new InvalidOperationException(IsolatedHostConstants.Failure);
        }
        var native = settings.Native ?? throw new InvalidOperationException(IsolatedHostConstants.Failure);
        var topology = settings.Selection.Options.Topology;
        target = settings.Selection.Target switch
        {
            IsolatedHostConstants.KeyLoad => CreateKeyLoad(settings, native),
            IsolatedHostConstants.SurrealDb => new SurrealDbTarget(CreateClient(native, PrimaryEndpointIndex), settings.RunId, native.Image, executionOptions),
            IsolatedHostConstants.HelixDb => new HelixDbTarget(CreateClient(native, PrimaryEndpointIndex), settings.RunId, native.Image, executionOptions),
            IsolatedHostConstants.Postgres => new PostgresTarget(native.Connection!, settings.RunId, native.Image, executionOptions, lifecycleOptions, topology),
            IsolatedHostConstants.Qdrant => CreateQdrant(settings, native),
            IsolatedHostConstants.Rabbit => new RabbitTarget(native.Connection!, settings.RunId, native.Image, lifecycleOptions, topology, CreateClient(native, PrimaryEndpointIndex)),
            IsolatedHostConstants.Redis => new RedisTarget(native.Connection!, settings.RunId, native.Image, lifecycleOptions, topology, [.. native.Replicas]),
            IsolatedHostConstants.Neo4j => new Neo4jTarget(CreateClient(native, PrimaryEndpointIndex), settings.RunId, native.Image, lifecycleOptions, executionOptions),
            IsolatedHostConstants.Mongo => new MongoTarget(native.Connection!, settings.RunId, native.Image, topology, lifecycleOptions, executionOptions),
            IsolatedHostConstants.OpenSearch => new OpenSearchTarget(CreateClient(native, PrimaryEndpointIndex), settings.RunId, native.Image, topology, lifecycleOptions, executionOptions),
            IsolatedHostConstants.Kurrent => new KurrentTarget(native.Connection!, CreateClients(native), settings.RunId, native.Image, topology, lifecycleOptions),
            _ => throw new InvalidOperationException(IsolatedHostConstants.Failure)
        };
        unownedClients.Clear();
        return target;
    }

    internal IVectorComparisonTarget CreateVector(IsolatedHostSettings settings)
    {
        const int PrimaryEndpointIndex = 0;

        if (target is not null || vectorTarget is not null)
        {
            throw new InvalidOperationException(IsolatedHostConstants.Failure);
        }

        var native = settings.Native ?? throw new InvalidOperationException(IsolatedHostConstants.Failure);
        vectorTarget = settings.Selection.Target switch
        {
            IsolatedHostConstants.Postgres => new PostgresNativeVectorTarget(native.Connection!, settings.RunId, native.Image, settings.Selection.Options.Topology, executionOptions, lifecycleOptions),
            IsolatedHostConstants.Qdrant => new QdrantTarget(CreateClient(native, PrimaryEndpointIndex), settings.RunId, native.Image, executionOptions, lifecycleOptions,
                settings.Selection.Options.Topology, CreateClients(native)),
            IsolatedHostConstants.SurrealDb => new SurrealDbVectorTarget(CreateClient(native, PrimaryEndpointIndex), native.Image, settings.RunId, executionOptions),
            IsolatedHostConstants.HelixDb => new HelixDbVectorTarget(CreateClient(native, PrimaryEndpointIndex), native.Image, settings.RunId, executionOptions),
            _ => throw new InvalidOperationException(IsolatedHostConstants.Failure)
        };
        unownedClients.Clear();
        return vectorTarget;
    }

    private KeyLoadTarget CreateKeyLoad(IsolatedHostSettings settings, IsolatedHostNativeSettings native)
    {
        const int ClientsFirstIndex = 0;

        var clients = CreateClients(native);
        return new(clients[ClientsFirstIndex], native.AdminKey!, settings.RunId, lifecycleOptions, executionOptions, admissionOptions, clientOptions, translationOptions, native.Image, clients, settings.Selection.NodeCount)
        { RequireIsolatedAdmission = true };
    }

    private QdrantTarget CreateQdrant(IsolatedHostSettings settings, IsolatedHostNativeSettings native)
    {
        const int ClientsFirstIndex = 0;

        var clients = CreateClients(native);
        return new(clients[ClientsFirstIndex], settings.RunId, native.Image, executionOptions, lifecycleOptions, settings.Selection.Options.Topology, clients);
    }

    private HttpClient[] CreateClients(IsolatedHostNativeSettings native)
    {
        const int FirstEntryIndex = 0;

        var clients = new HttpClient[native.Endpoints.Length];
        for (var index = FirstEntryIndex; index < clients.Length; index++)
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
        var ownedVectorTarget = vectorTarget;
        target = null;
        vectorTarget = null;
        try
        {
            if (ownedTarget is not null)
            {
                await DisposeTargetAsync(ownedTarget);
            }
            if (ownedVectorTarget is not null)
            {
                var cleanup = ownedVectorTarget.DisposeAsync().AsTask();
                await cleanup.WaitAsync(Policy.CleanupTimeout);
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

    private async Task DisposeTargetAsync(IComparisonTarget ownedTarget)
    {
        var cleanup = ownedTarget.DisposeAsync().AsTask();
        try
        {
            await cleanup.WaitAsync(Policy.CleanupTimeout);
        }
        catch (Exception)
        {
            _ = cleanup.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw new ComparisonFailureException(IsolatedHostConstants.CleanupFailure);
        }
    }
}
