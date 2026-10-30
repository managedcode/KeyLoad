using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

internal sealed class ModelDatabaseTargets : IDisposable
{
    private readonly List<SocketsHttpHandler> handlers = [];
    private const string Endpoint = "Benchmarks__Execution__Endpoints__";
    private const string Connection = "Benchmarks__Execution__ConnectionString", Image = "Benchmarks__Execution__Image";
    private const string ApiKey = "Benchmarks__Execution__ApiKey", ApiKeyHeader = "api-key";
    private const string User = "Benchmarks__Execution__User", Password = "Benchmarks__Execution__Password";
    private const int First = 0, Single = 1;

    internal IComparisonTarget Create(string target, IReadOnlyDictionary<string, string> values, string runId,
        IOptions<ComparisonExecutionOptions> execution, IConfiguration configuration)
    {
        var topology = NativeDatabaseTargets.Topology(values);
        var endpoints = values.Where(pair => pair.Key.StartsWith(Endpoint, StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value);
        var clients = new List<HttpClient>();
        try
        {
            foreach (var endpoint in target == IsolatedHostConstants.Rabbit ? endpoints.Take(Single) : endpoints)
            {
                var client = CreateClient(endpoint, target != IsolatedHostConstants.Kurrent);
                clients.Add(client);
                if (values.TryGetValue(ApiKey, out var key))
                {
                    client.DefaultRequestHeaders.Add(ApiKeyHeader, key);
                }
                if (target == IsolatedHostConstants.Rabbit)
                {
                    var credentials = values[User] + ":" + values[Password];
                    client.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(credentials)));
                }
            }
            var lifecycle = ComparisonExecutionRegistration.ReadLifecycle(configuration);
            IComparisonTarget result = target switch
            {
                IsolatedHostConstants.Qdrant => new QdrantTarget(clients[First], runId, values[Image], execution, lifecycle,
                    topology, [.. clients]),
                IsolatedHostConstants.Rabbit => new RabbitTarget(values[Connection], runId, values[Image], lifecycle,
                    topology, clients[First]),
                IsolatedHostConstants.Kurrent => new KurrentTarget(values[Connection], [.. clients], runId, values[Image],
                    topology, lifecycle, ComparisonExecutionRegistration.ReadDiagnostics(configuration)),
                _ => throw new ArgumentOutOfRangeException(nameof(target))
            };
            clients.Clear();
            return result;
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    private HttpClient CreateClient(string endpoint, bool allowRedirects)
    {
        var address = new Uri(endpoint);
        var handler = new SocketsHttpHandler { AllowAutoRedirect = allowRedirects };
        handlers.Add(handler);
        var client = new HttpClient(handler, disposeHandler: false);
        try
        {
            client.BaseAddress = address;
            client.Timeout = Timeout.InfiniteTimeSpan;
            return client;
        }
        catch (Exception)
        {
            client.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var handler in handlers)
        {
            handler.Dispose();
        }
        handlers.Clear();
    }
}
