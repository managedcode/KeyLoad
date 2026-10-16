using System.Net.Http.Headers;
using System.Text;
using KeyLoad.Client;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Owns only one native target and tracks partial HTTP construction until ownership transfers.</summary>
internal sealed class IsolatedHostTargetOwner(IOptions<NativeComparisonExecutionOptions> executionOptions,
    IOptions<NativeComparisonDiagnosticOptions> diagnosticOptions,
    IOptions<IsolatedKeyLoadAdmissionOptions> admissionOptions,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions, IOptions<KeyLoadClientExecutionOptions> clientOptions, IOptions<QueryTranslationOptions> translationOptions, TimeProvider? provider = null) : IAsyncDisposable
{
    internal TimeProvider Clock { get; } = provider ?? TimeProvider.System;
    private readonly IOptions<NativeComparisonDiagnosticOptions> diagnostics = NativeComparisonDiagnosticOptions.Require(diagnosticOptions);
    private readonly IOptions<NativeComparisonExecutionOptions> execution = NativeComparisonExecutionOptions.Require(executionOptions);
    private NativeComparisonExecutionOptions Policy => execution.Value;
    internal IOptions<NativeComparisonExecutionOptions> ExecutionOptions => execution;
    private readonly List<HttpClient> unownedClients = [];
    private IComparisonTarget? target;

    internal TargetProfile? ObservedProfile => target?.Profile;

    internal IComparisonTarget Create(IsolatedHostSettings settings)
    {
        InitializeTarget(settings);
        return target!;
    }

    internal BenchmarkSession CreateSession(IsolatedHostSettings settings, Action<DocumentComparisonProgress> progress)
    {
        InitializeTarget(settings);
        return new(target!, execution, new BenchmarkSessionOptions(), Clock, progress);
    }

    private void InitializeTarget(IsolatedHostSettings settings)
    {
        const int PrimaryEndpointIndex = 0;

        if (target is not null)
        {
            throw new InvalidOperationException(IsolatedHostConstants.Failure);
        }
        var native = settings.Native ?? throw new InvalidOperationException(IsolatedHostConstants.Failure);
        var topology = settings.Selection.Topology;
        target = settings.Selection.Target switch
        {
            IsolatedHostConstants.KeyLoad => CreateKeyLoad(settings, native),
            IsolatedHostConstants.SurrealDb => new SurrealDbTarget(CreateClient(native, PrimaryEndpointIndex), settings.RunId, native.Image, executionOptions, provider: Clock),
            IsolatedHostConstants.HelixDb => new HelixDbTarget(CreateClient(native, PrimaryEndpointIndex), settings.RunId, native.Image, executionOptions, provider: Clock),
            IsolatedHostConstants.Postgres => new PostgresTarget(native.Connection!, settings.RunId, native.Image, executionOptions, lifecycleOptions, topology, provider: Clock),
            IsolatedHostConstants.Qdrant => CreateQdrant(settings, native),
            IsolatedHostConstants.Rabbit => new RabbitTarget(native.Connection!, settings.RunId, native.Image, lifecycleOptions, topology, CreateClient(native, PrimaryEndpointIndex), provider: Clock),
            IsolatedHostConstants.Redis => new RedisTarget(native.Connection!, settings.RunId, native.Image, lifecycleOptions, executionOptions, diagnostics, topology, [.. native.Replicas], provider: Clock),
            IsolatedHostConstants.Neo4j => new Neo4jTarget(CreateClient(native, PrimaryEndpointIndex), settings.RunId, native.Image, lifecycleOptions, executionOptions, provider: Clock),
            IsolatedHostConstants.Mongo => new MongoTarget(native.Connection!, settings.RunId, native.Image, topology, lifecycleOptions, executionOptions, provider: Clock),
            IsolatedHostConstants.OpenSearch => new OpenSearchTarget(CreateClient(native, PrimaryEndpointIndex), settings.RunId, native.Image, topology, lifecycleOptions, executionOptions, provider: Clock),
            IsolatedHostConstants.Kurrent => new KurrentTarget(native.Connection!, CreateClients(native), settings.RunId, native.Image, topology, lifecycleOptions, diagnostics, provider: Clock),
            _ => throw new InvalidOperationException(IsolatedHostConstants.Failure)
        };
        unownedClients.Clear();
    }

    private KeyLoadTarget CreateKeyLoad(IsolatedHostSettings settings, IsolatedHostNativeSettings native)
    {
        const int ClientsFirstIndex = 0;

        var clients = CreateClients(native);
        return new(clients[ClientsFirstIndex], native.AdminKey!, settings.RunId, lifecycleOptions, executionOptions, diagnostics, admissionOptions, clientOptions, translationOptions, native.Image, clients, settings.Selection.NodeCount, provider: Clock)
        {
            RequireIsolatedAdmission = true
        };
    }

    private QdrantTarget CreateQdrant(IsolatedHostSettings settings, IsolatedHostNativeSettings native)
    {
        const int ClientsFirstIndex = 0;

        var clients = CreateClients(native);
        return new(clients[ClientsFirstIndex], settings.RunId, native.Image, executionOptions, lifecycleOptions, settings.Selection.Topology, clients, provider: Clock);
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

    private async Task DisposeTargetAsync(IComparisonTarget ownedTarget)
    {
        var cleanup = ownedTarget.DisposeAsync().AsTask();
        try
        {
            await cleanup.WaitAsync(Policy.CleanupTimeout, Clock);
        }
        catch (TimeoutException)
        {
            _ = cleanup.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw new ComparisonFailureException(IsolatedHostConstants.CleanupFailure);
        }
    }
}
