using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ManagedCode.Communication;
using StorageMetadata = ManagedCode.Storage.Core.Models.BlobMetadata;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Requires original bytes through the execution Storage server; old MCP chunk calls cannot satisfy this gate.</summary>
internal static class IsolatedKeyLoadPublicChecksBlobs
{
    private const int ContentLength = 65_545;
    private const int ByteCycle = 251;
    private const string Prefix = "/api/storage";
    private const string FileName = "public-object.bin";
    private const string FormField = "file";
    private const string Scheme = "Bearer";

    internal static async Task VerifyAsync(HttpClient http, string credential,
        IsolatedKeyLoadPublicChecksScenario scenario, CancellationToken token)
    {
        var partition = scenario.Partition;
        var scope = string.Join('/', Prefix, Uri.EscapeDataString(partition.TenantId), Uri.EscapeDataString(partition.DatabaseId),
            Uri.EscapeDataString(partition.TransactionDomainId), Uri.EscapeDataString(partition.PartitionKey), IsolatedKeyLoadPublicChecksScenario.FileResource);
        var bytes = Enumerable.Range(0, ContentLength).Select(index => (byte)(index % ByteCycle)).ToArray();
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes);
        form.Add(content, FormField, FileName);
        using var upload = new HttpRequestMessage(HttpMethod.Post, string.Join('/', scope, "upload")) { Content = form };
        upload.Headers.Authorization = new AuthenticationHeaderValue(Scheme, credential);
        using var published = await http.SendAsync(upload, HttpCompletionOption.ResponseHeadersRead, token);
        await Assert.That(published.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var metadata = await published.Content.ReadFromJsonAsync<Result<StorageMetadata>>(JsonDefaults.Options, token);
        await Assert.That(metadata.IsSuccess).IsTrue();
        await Assert.That(metadata.Value?.Length).IsEqualTo((ulong)bytes.Length);
        using var read = new HttpRequestMessage(HttpMethod.Get, string.Join('/', scope, "download", FileName));
        read.Headers.Authorization = new AuthenticationHeaderValue(Scheme, credential);
        using var downloaded = await http.SendAsync(read, HttpCompletionOption.ResponseHeadersRead, token);
        await Assert.That(downloaded.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var actual = await downloaded.Content.ReadAsByteArrayAsync(token);
        await Assert.That(actual.AsSpan().SequenceEqual(bytes)).IsTrue();
    }
}
