using System.Net.Http.Headers;
using System.Text;
using KeyLoad.Client;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Owns only one execution target and tracks partial HTTP construction until ownership transfers.</summary>
internal sealed class IsolatedHostTargetOwner(IOptions<ComparisonExecutionOptions> executionOptions,
    IOptions<ComparisonDiagnosticOptions> diagnosticOptions,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions, IOptions<KeyLoadClientExecutionOptions> clientOptions, IOptions<QueryTranslationOptions> translationOptions, TimeProvider? provider = null) : IAsyncDisposable
{
    private const string ImageRequired = "A database comparison requires its configured container image.";
    internal TimeProvider Clock { get; } = provider ?? TimeProvider.System;
    private readonly IOptions<ComparisonDiagnosticOptions> diagnostics = ComparisonDiagnosticOptions.Require(diagnosticOptions);
    private readonly IOptions<ComparisonExecutionOptions> execution = ComparisonExecutionOptions.Require(executionOptions);
    private ComparisonExecutionOptions Policy => execution.Value;
    internal IOptions<ComparisonExecutionOptions> ExecutionOptions => execution;
    private readonly List<HttpClient> unownedClients = [];
    private readonly List<SocketsHttpHandler> clientHandlers = [];
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
        var execution = settings.Execution ?? throw new InvalidOperationException(IsolatedHostConstants.Failure);
        var topology = settings.Selection.Topology;
        target = settings.Selection.Target switch
        {
            IsolatedHostConstants.KeyLoad => CreateKeyLoad(settings, execution),
            IsolatedHostConstants.SurrealDb => new SurrealDbTarget(CreateClient(execution, PrimaryEndpointIndex), settings.RunId, RequireImage(execution), executionOptions, provider: Clock),
            IsolatedHostConstants.HelixDb => new HelixDbTarget(CreateClient(execution, PrimaryEndpointIndex), settings.RunId, RequireImage(execution), executionOptions, provider: Clock),
            IsolatedHostConstants.Postgres => new PostgresTarget(execution.Connection!, settings.RunId, RequireImage(execution), executionOptions, lifecycleOptions, topology, provider: Clock),
            IsolatedHostConstants.Qdrant => CreateQdrant(settings, execution),
            IsolatedHostConstants.Rabbit => new RabbitTarget(execution.Connection!, settings.RunId, RequireImage(execution), lifecycleOptions, topology, CreateClient(execution, PrimaryEndpointIndex), provider: Clock),
            IsolatedHostConstants.Redis => new RedisTarget(execution.Connection!, settings.RunId, RequireImage(execution), lifecycleOptions, executionOptions, diagnostics, topology, [.. execution.Replicas], provider: Clock),
            IsolatedHostConstants.Neo4j => new Neo4jTarget(CreateClient(execution, PrimaryEndpointIndex), settings.RunId, RequireImage(execution), lifecycleOptions, executionOptions, provider: Clock),
            IsolatedHostConstants.Mongo => new MongoTarget(execution.Connection!, execution.Endpoints, settings.RunId, RequireImage(execution), topology, lifecycleOptions, executionOptions, provider: Clock),
            IsolatedHostConstants.OpenSearch => new OpenSearchTarget(CreateClient(execution, PrimaryEndpointIndex), settings.RunId, RequireImage(execution), topology, lifecycleOptions, executionOptions, provider: Clock),
            IsolatedHostConstants.Kurrent => new KurrentTarget(execution.Connection!, CreateClients(execution), settings.RunId, RequireImage(execution), topology, lifecycleOptions, diagnostics, provider: Clock),
            _ => throw new InvalidOperationException(IsolatedHostConstants.Failure)
        };
        unownedClients.Clear();
    }

    private static string RequireImage(RuntimeIsolatedHostSettings execution)
        => !string.IsNullOrWhiteSpace(execution.Image) ? execution.Image
            : throw new InvalidOperationException(ImageRequired);

    private KeyLoadTarget CreateKeyLoad(IsolatedHostSettings settings, RuntimeIsolatedHostSettings execution)
    {
        const int ClientsFirstIndex = 0;

        var clients = CreateClients(execution);
        return new(clients[ClientsFirstIndex], execution.AdminKey!, settings.RunId, lifecycleOptions, executionOptions, diagnostics, clientOptions, translationOptions, RequireImage(execution), clients, settings.Selection.NodeCount, provider: Clock);
    }

    private QdrantTarget CreateQdrant(IsolatedHostSettings settings, RuntimeIsolatedHostSettings execution)
    {
        const int ClientsFirstIndex = 0;

        var clients = CreateClients(execution);
        return new(clients[ClientsFirstIndex], settings.RunId, RequireImage(execution), executionOptions, lifecycleOptions, settings.Selection.Topology, clients, provider: Clock);
    }

    private HttpClient[] CreateClients(RuntimeIsolatedHostSettings execution)
    {
        const int FirstEntryIndex = 0;

        var clients = new HttpClient[execution.Endpoints.Length];
        for (var index = FirstEntryIndex; index < clients.Length; index++)
        {
            clients[index] = CreateClient(execution, index);
        }
        return clients;
    }

    private HttpClient CreateClient(RuntimeIsolatedHostSettings execution, int index)
    {
        var handler = new SocketsHttpHandler
        {
            MaxResponseHeadersLength = this.execution.Value.DocumentClientMaxResponseHeadersKilobytes
        };
        clientHandlers.Add(handler);
        var client = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = execution.Endpoints[index],
            Timeout = Timeout.InfiniteTimeSpan
        };
        unownedClients.Add(client);
        if (execution.User is not null)
        {
            var credential = execution.User + IsolatedHostConstants.CredentialSeparator + execution.Password;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(IsolatedHostConstants.Basic,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(credential)));
        }
        if (execution.ApiKey is not null)
        {
            client.DefaultRequestHeaders.Add(ComparisonHostConstants.QdrantApiKeyHeader, execution.ApiKey);
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
            foreach (var handler in clientHandlers)
            {
                handler.Dispose();
            }
            clientHandlers.Clear();
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
