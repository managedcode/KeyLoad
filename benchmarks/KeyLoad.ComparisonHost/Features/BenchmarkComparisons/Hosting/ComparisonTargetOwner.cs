using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Owns target construction and each client until its target takes ownership.</summary>
internal sealed class ComparisonTargetOwner(IOptions<NativeComparisonExecutionOptions> executionOptions) : IAsyncDisposable
{
    private readonly List<IComparisonTarget> targets = new(ComparisonHostConstants.TargetCount);
    private readonly List<HttpClient> unownedClients = new(ComparisonHostConstants.HttpClientCount);
    private IComparisonTarget? pendingTarget;

    /// <summary>Creates targets in the established comparison order.</summary>
    /// <param name="settings">Validated comparison settings.</param>
    /// <returns>The target list consumed by the existing runner.</returns>
    internal IComparisonTarget[] CreateTargets(ComparisonHostSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var keyLoadClients = CreateClients(settings.KeyLoadEndpoints);
        var keyLoadClient = keyLoadClients[0];
        var qdrantClient = CreateClient(settings.QdrantEndpoint);
        qdrantClient.DefaultRequestHeaders.Add(ComparisonHostConstants.QdrantApiKeyHeader, settings.QdrantApiKey);
        var neo4jClient = CreateClient(settings.Neo4jEndpoint);
        neo4jClient.DefaultRequestHeaders.Authorization = CreateNeo4jAuthorization(settings.Neo4jPassword);

        pendingTarget = new KeyLoadTarget(keyLoadClient, settings.AdminKey, settings.RunId,
            image: settings.ExecutionIdentity?.KeyLoadImage, peers: keyLoadClients);
        PublishPendingTarget(keyLoadClients);
        pendingTarget = new PostgresTarget(settings.PostgresConnection, settings.RunId, settings.PostgresImage);
        PublishPendingTarget();
        pendingTarget = new QdrantTarget(qdrantClient, settings.RunId, settings.QdrantImage, settings.Options.Topology, null, executionOptions);
        PublishPendingTarget(qdrantClient);
        var rabbitManagementClient = CreateClient(settings.RabbitManagementEndpoint);
        rabbitManagementClient.DefaultRequestHeaders.Authorization =
            ComparisonEndpointBindings.CreateRabbitAuthorization(settings.RabbitUser, settings.RabbitPassword);
        pendingTarget = new RabbitTarget(settings.RabbitConnection, settings.RunId, settings.RabbitImage,
            management: rabbitManagementClient);
        PublishPendingTarget(rabbitManagementClient);
        pendingTarget = new RedisTarget(settings.RedisConnection, settings.RunId, settings.RedisImage);
        PublishPendingTarget();
        pendingTarget = new Neo4jTarget(neo4jClient, settings.RunId, settings.Neo4jImage);
        PublishPendingTarget(neo4jClient);
        return [.. targets];
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisposeTargetsAsync(0);
        }
        finally
        {
            try
            {
                await DisposePendingTargetAsync();
            }
            finally
            {
                try
                {
                    await DisposeUnownedClientsAsync(0);
                }
                finally
                {
                    DetachOwners();
                }
            }
        }
    }

    private async Task DisposeTargetsAsync(int index)
    {
        if (index >= targets.Count)
        {
            return;
        }

        try
        {
            await DisposeTargetAsync(targets[index]);
        }
        finally
        {
            await DisposeTargetsAsync(index + 1);
        }
    }

    private async Task DisposePendingTargetAsync()
    {
        if (pendingTarget is null)
        {
            return;
        }

        try
        {
            await pendingTarget.DisposeAsync();
        }
        catch (Exception error)
        {
            await WriteTargetCleanupFailureAsync(pendingTarget, error);
            throw new ComparisonFailureException(ComparisonHostConstants.TargetCleanupFailedCode);
        }
    }

    private static async Task DisposeTargetAsync(IComparisonTarget target)
    {
        try
        {
            await target.DisposeAsync();
        }
        catch (Exception error)
        {
            await WriteTargetCleanupFailureAsync(target, error);
            throw new ComparisonFailureException(ComparisonHostConstants.TargetCleanupFailedCode);
        }
    }

    private static async Task WriteTargetCleanupFailureAsync(IComparisonTarget target, Exception error)
    {
        try
        {
            await Console.Error.WriteLineAsync(target.Profile.Name + ComparisonHostConstants.CleanupFailureSuffix +
                error.GetType().Name);
        }
        catch (Exception)
        {
            throw new ComparisonFailureException(ComparisonHostConstants.TargetCleanupFailedCode);
        }
    }

    private async Task DisposeUnownedClientsAsync(int index)
    {
        if (index >= unownedClients.Count)
        {
            return;
        }

        try
        {
            await DisposeUnownedClient(unownedClients[index]);
        }
        finally
        {
            await DisposeUnownedClientsAsync(index + 1);
        }
    }

    private static async Task DisposeUnownedClient(HttpClient client)
    {
        try
        {
            client.Dispose();
        }
        catch (Exception error)
        {
            await WriteUnownedClientCleanupFailureAsync(error);
            throw new ComparisonFailureException(ComparisonHostConstants.TargetCleanupFailedCode);
        }
    }

    private static async Task WriteUnownedClientCleanupFailureAsync(Exception error)
    {
        try
        {
            await Console.Error.WriteLineAsync(ComparisonHostConstants.UnownedClientName +
                ComparisonHostConstants.CleanupFailureSuffix + error.GetType().Name);
        }
        catch (Exception)
        {
            throw new ComparisonFailureException(ComparisonHostConstants.TargetCleanupFailedCode);
        }
    }

    private void DetachOwners()
    {
        targets.Clear();
        pendingTarget = null;
        unownedClients.Clear();
    }

    private HttpClient CreateClient(Uri endpoint)
    {
        var client = new HttpClient
        {
            BaseAddress = endpoint,
            Timeout = ComparisonHostConstants.InfiniteTimeout
        };
        unownedClients.Add(client);
        return client;
    }

    private HttpClient[] CreateClients(IReadOnlyList<Uri> endpoints)
    {
        var clients = new HttpClient[ComparisonHostConstants.KeyLoadEndpointCount];
        for (var index = 0; index < clients.Length; index++)
        {
            clients[index] = CreateClient(endpoints[index]);
        }

        return clients;
    }

    private void PublishPendingTarget(params HttpClient[] transferredClients)
    {
        var target = pendingTarget ?? throw new UnreachableException();
        foreach (var client in transferredClients)
        {
            unownedClients.Remove(client);
        }

        targets.Add(target);
        pendingTarget = null;
    }

    private static AuthenticationHeaderValue CreateNeo4jAuthorization(string password)
    {
        var credential = ComparisonHostConstants.Neo4jUser + ComparisonHostConstants.UserPasswordSeparator + password;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential));
        return new AuthenticationHeaderValue(ComparisonHostConstants.BasicAuthenticationScheme, token);
    }
}
