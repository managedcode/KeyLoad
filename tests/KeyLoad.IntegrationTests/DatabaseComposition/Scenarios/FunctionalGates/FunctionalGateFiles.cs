using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;
using StorageMetadata = ManagedCode.Storage.Core.Models.BlobMetadata;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Requires real execution file transfers; removing the old protocol cannot make this gate pass.</summary>
internal static class FunctionalGateFiles
{
    private const int ResourceCount = 3;
    private const int PayloadLength = 65_545;
    private const int ByteCycle = 251;
    private const string FormField = "file";
    private const string RuntimePrefix = "/api/storage";

    internal static async Task InitializeAsync(FunctionalGateContext context, CancellationToken token)
    {
        for (var index = 0; index < ResourceCount; index++)
        {
            var name = $"gate_blobs_{index}";
            var definition = new ResourceDefinition(name, ResourceKind.BlobStore, context.Partition.TransactionDomainId);
            await SqlClusterProtocol.EqualAsync(definition,
                await context.SdkAsync(context.Administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                    new(context.Partition.TenantId, context.Partition.DatabaseId, definition), token)));
            var file = await UploadAsync(context, name, "baseline", token);
            await VerifyAsync(context.Reader, file, token);
        }
    }

    internal static async Task ExerciseAsync(FunctionalGateContext context, int iteration, int resourceIndex, CancellationToken token)
    {
        var file = await UploadAsync(context, $"gate_blobs_{resourceIndex}", $"iteration_{iteration}", token);
        await VerifyAsync(context.Reader, file, token);
        using var deniedContent = new ByteArrayContent(file.Bytes);
        using var deniedRequest = UploadRequest(file.Path, deniedContent);
        using var denied = await context.Reader.SendStorageAsync(deniedRequest, token);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await VerifyAsync(context.Reader, file, token);
    }

    internal static async Task<FunctionalGateFile> UploadAsync(FunctionalGateContext context, string resource,
        string name, CancellationToken token)
    {
        var scope = context.Partition;
        var path = string.Join('/', RuntimePrefix, Uri.EscapeDataString(scope.TenantId), Uri.EscapeDataString(scope.DatabaseId),
            Uri.EscapeDataString(scope.TransactionDomainId), Uri.EscapeDataString(scope.PartitionKey), Uri.EscapeDataString(resource));
        var bytes = Enumerable.Range(0, PayloadLength).Select(index => (byte)(index % ByteCycle)).ToArray();
        SHA256.HashData(Encoding.UTF8.GetBytes(path + name)).CopyTo(bytes, 0);
        using var content = new ByteArrayContent(bytes);
        using var request = UploadRequest(string.Join('/', path, name), content);
        using var response = await context.Writer.SendStorageAsync(request, token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var uploaded = await response.Content.ReadFromJsonAsync<Result<StorageMetadata>>(JsonDefaults.Options, token);
        await Assert.That(uploaded.IsSuccess).IsTrue();
        await Assert.That(uploaded.Value?.Length).IsEqualTo((ulong)bytes.Length);
        var file = new FunctionalGateFile(string.Join('/', path, name), bytes);
        await VerifyAsync(context.Reader, file, token);
        return file;
    }

    internal static async Task VerifyAsync(RequestCqrsClusterCallers caller, FunctionalGateFile file, CancellationToken token)
    {
        var separator = file.Path.LastIndexOf('/');
        var path = string.Concat(file.Path.AsSpan(0, separator), "/download", file.Path.AsSpan(separator));
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await caller.SendStorageAsync(request, token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var actual = await response.Content.ReadAsByteArrayAsync(token);
        await Assert.That(actual.AsSpan().SequenceEqual(file.Bytes)).IsTrue();
    }

    private static HttpRequestMessage UploadRequest(string path, HttpContent body)
    {
        var separator = path.LastIndexOf('/');
        var content = new MultipartFormDataContent
        {
            { body, FormField, path[(separator + 1)..] }
        };
        return new(HttpMethod.Post, string.Concat(path.AsSpan(0, separator), "/upload")) { Content = content };
    }
}
