using System.Globalization;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Independent public operation names and canonical SQL arguments passed directly to real SDK clients.</summary>
internal static class IsolatedKeyLoadPublicRegressionProtocol
{
    internal const string Commit = "keyload_documents_commit";
    internal const string Get = "keyload_documents_get";
    internal const string StreamRead = "keyload_streams_read";
    internal const string Inspect = "keyload_messages_inspect";
    internal const string Receive = "keyload_messages_receive";
    internal const string Complete = "keyload_messages_complete";
    internal const string BlobPart = "keyload_blobs_write_part";
    internal const string BlobComplete = "keyload_blobs_complete_upload";
    internal const string BlobMetadata = "keyload_blobs_metadata";
    internal const string BlobRange = "keyload_blobs_read_range";
    internal const string Request = "request";
    internal const int CleanupSeconds = 30;
    private const string Argument = "args";
    private const string NodePrefix = "node";
    private const string HttpEndpoint = "http";
    private const string CallPrefix = "CALL ";
    private const string CallSuffix = "(@args)";

    internal static HttpClient CreateHttp(DistributedApplication app, int node)
    {
        var http = app.CreateHttpClient(NodePrefix + node.ToString(CultureInfo.InvariantCulture), HttpEndpoint);
        http.Timeout = TimeSpan.FromSeconds(30);
        return http;
    }

    internal static SqlOperationRequest Call<T>(PartitionRef partition, string operation, T request)
        => new(partition, CallPrefix + operation + CallSuffix, new()
        {
            [Argument] = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            { [Request] = JsonSerializer.SerializeToElement(request, JsonDefaults.Options) }, JsonDefaults.Options)
        });

    internal static async Task<T> SqlAsync<T>(KeyLoadClient sdk, SqlOperationRequest request, CancellationToken token)
        => (await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.ExecuteSqlAsync(request, token)))
            .Deserialize<T>(JsonDefaults.Options)!;
}
