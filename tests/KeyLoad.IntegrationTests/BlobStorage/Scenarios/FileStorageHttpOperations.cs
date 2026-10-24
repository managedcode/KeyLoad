using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KeyLoad.IntegrationTests.DatabaseComposition;
using KeyLoad.IntegrationTests.OrleansExecution;
using ManagedCode.Communication;
using ManagedCode.Storage.Core.Models;

namespace KeyLoad.IntegrationTests.BlobStorage;

internal static class FileStorageHttpOperations
{
    private const char Separator = '/';
    private const int Start = 3;
    private const int End = 17;
    private const string StreamRoute = "/stream/";
    private const string DownloadRoute = "/download/";
    private const string MetadataRoute = "/metadata/";
    private const string ListRoute = "/list";
    private const string DeleteRoute = "/files/";

    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateFile file, CancellationToken token)
    {
        var separator = file.Path.LastIndexOf(Separator);
        var root = file.Path[..separator];
        var name = file.Path[(separator + 1)..];
        using var range = new HttpRequestMessage(HttpMethod.Get, root + StreamRoute + name);
        range.Headers.Range = new RangeHeaderValue(Start, End);
        using var response = await context.Reader.SendStorageAsync(range, token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.PartialContent);
        await Assert.That((await response.Content.ReadAsByteArrayAsync(token)).AsSpan().SequenceEqual(file.Bytes.AsSpan(Start, End - Start + 1))).IsTrue();
        using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, root + MetadataRoute + name);
        using var metadata = await context.Reader.SendStorageAsync(metadataRequest, token);
        await Assert.That(metadata.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var entry = await metadata.Content.ReadFromJsonAsync<Result<BlobMetadata>>(JsonDefaults.Options, token);
        await Assert.That(entry.Value!.FullName).IsEqualTo(name);
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, root + ListRoute);
        using var list = await context.Reader.SendStorageAsync(listRequest, token);
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var entries = await list.Content.ReadFromJsonAsync<Result<BlobMetadata[]>>(JsonDefaults.Options, token);
        await Assert.That(entries.Value!.Any(value => value.FullName == name)).IsTrue();
        using var deniedRequest = new HttpRequestMessage(HttpMethod.Delete, root + DeleteRoute + name);
        using var denied = await context.Reader.SendStorageAsync(deniedRequest, token);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await FunctionalGateFiles.VerifyAsync(context.Reader, file, token);
    }

    internal static async Task RequireDeniedReadAsync(RequestCqrsClusterCallers caller, FunctionalGateFile file, CancellationToken token)
    {
        var separator = file.Path.LastIndexOf(Separator);
        var route = file.Path[0..separator] + DownloadRoute + file.Path[(separator + 1)..];
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        using var response = await caller.SendStorageAsync(request, token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }
}
