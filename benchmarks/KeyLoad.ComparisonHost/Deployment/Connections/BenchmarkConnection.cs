using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace KeyLoad.ComparisonHost.Deployment;

/// <summary>Endpoints and credentials the AppHost published for the deployed database.</summary>
/// <remarks>A setting the database does not publish is an empty string.</remarks>
internal sealed record BenchmarkConnection(string Database, int Nodes, string Image, string ConnectionString,
    IReadOnlyList<Uri> Endpoints, IReadOnlyList<string> Replicas, string User, string Password, string ApiKey, string AdminKey)
{
    private const string Prefix = "Benchmarks__Execution__";
    private const string ImageKey = Prefix + "Image";
    private const string ConnectionKey = Prefix + "ConnectionString";
    private const string EndpointPrefix = Prefix + "Endpoints__";
    private const string ReplicaPrefix = Prefix + "ReplicaConnections__";
    private const string UserKey = Prefix + "User";
    private const string PasswordKey = Prefix + "Password";
    private const string ApiKeyKey = Prefix + "ApiKey";
    private const string AdminKeyKey = "Benchmarks__AdminKey";
    private const string ApiKeyHeader = "api-key";
    private const string Basic = "Basic";
    private const string Missing = "The deployed database did not publish the required setting: ";

    internal string RequireConnectionString() => Require(ConnectionString, ConnectionKey);

    internal string RequireAdminKey() => Require(AdminKey, AdminKeyKey);

    internal static BenchmarkConnection Read(IReadOnlyDictionary<string, string> values, BenchmarkJob job)
        => new(job.Database, job.Nodes, Value(values, ImageKey), Value(values, ConnectionKey), ReadEndpoints(values),
            Indexed(values, ReplicaPrefix), Value(values, UserKey), Value(values, PasswordKey), Value(values, ApiKeyKey),
            Value(values, AdminKeyKey));

    /// <summary>Creates an HTTP client for one node; the caller owns and disposes it.</summary>
    internal HttpClient CreateHttpClient(int node, bool allowRedirects = true)
    {
        SocketsHttpHandler? handler = new()
        {
            AllowAutoRedirect = allowRedirects,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        HttpClient? pending = null;
        try
        {
            pending = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = Endpoints[node],
                Timeout = Timeout.InfiniteTimeSpan
            };
            handler = null;
            Authenticate(pending);
            var client = pending;
            pending = null;
            return client;
        }
        finally
        {
            handler?.Dispose();
            pending?.Dispose();
        }
    }

    private void Authenticate(HttpClient client)
    {
        if (User.Length > 0)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(Basic,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(User + ":" + Password)));
        }
        if (ApiKey.Length > 0)
        {
            client.DefaultRequestHeaders.Add(ApiKeyHeader, ApiKey);
        }
    }

    private static string Value(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : string.Empty;

    private static string Require(string value, string key)
        => value.Length > 0 ? value : throw new InvalidOperationException(Missing + key);

    private static List<Uri> ReadEndpoints(IReadOnlyDictionary<string, string> values)
        => [.. Indexed(values, EndpointPrefix).Select(value => new Uri(value, UriKind.Absolute))];

    private static List<string> Indexed(IReadOnlyDictionary<string, string> values, string prefix)
    {
        var result = new List<string>();
        while (values.TryGetValue(prefix + result.Count.ToString(CultureInfo.InvariantCulture), out var value))
        {
            result.Add(value);
        }
        return result;
    }
}
