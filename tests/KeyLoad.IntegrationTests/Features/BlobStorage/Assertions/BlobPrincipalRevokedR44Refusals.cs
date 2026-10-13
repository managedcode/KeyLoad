using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using ManagedCode.Communication;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPrincipalRevokedR44Refusals
{
    private const string MetadataTool = "keyload_blobs_metadata";
    private const string UnavailableDetail = "The credential is unavailable or expired.";
    internal static async Task LiveAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        BlobPrincipalRevokedR44State state, CancellationToken token)
    {
        var metadata = new BlobMetadataRequest(state.Published.Begin.Blob);
        await SdkAsync(reader.Sdk.GetBlobMetadataAsync(metadata, token), admin, state, token);
        await McpAsync(async () => { _ = await reader.Mcp.CallAsync(MetadataTool, metadata, token); }, admin, state, token);
        foreach (var range in BlobPrincipalRevokedR44Pages.Ranges)
        {
            var request = new BlobReadRequest(state.Published.Begin.Blob, 1L, range.Offset, range.Count);
            await SdkAsync(reader.Sdk.ReadBlobRangeAsync(request, token), admin, state, token);
            await McpAsync(async () => { _ = await reader.Mcp.CallAsync(BlobPrincipalRevokedR44Pages.RangeTool, request, token); }, admin, state, token);
        }
    }
    internal static async Task SdkOnlyAsync(KeyLoadClient sdk, RequestCqrsRf3Callers admin,
        BlobPrincipalRevokedR44State state, CancellationToken token)
    {
        await SdkAsync(sdk.GetBlobMetadataAsync(new(state.Published.Begin.Blob), token), admin, state, token);
        foreach (var range in BlobPrincipalRevokedR44Pages.Ranges)
        {
            await SdkAsync(sdk.ReadBlobRangeAsync(new(state.Published.Begin.Blob, 1L, range.Offset, range.Count), token), admin, state, token);
        }
    }
    private static async Task SdkAsync<T>(Task<Result<T>> operation, RequestCqrsRf3Callers admin,
        BlobPrincipalRevokedR44State state, CancellationToken token)
    {
        var result = await operation;
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Unauthenticated, UnavailableDetail), result.Problem);
        await BlobPrincipalRevokedR44Pages.CurrentAsync(admin, state, token);
    }
    private static async Task McpAsync(Func<Task> operation, RequestCqrsRf3Callers admin,
        BlobPrincipalRevokedR44State state, CancellationToken token)
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(operation);
        await Assert.That(error!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error!.Message.Contains(state.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await BlobPrincipalRevokedR44Pages.CurrentAsync(admin, state, token);
    }
    internal static async Task InitializeAsync(ClusterFixture fixture, RequestCqrsRf3Callers admin,
        BlobPrincipalRevokedR44State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            McpOfficialClient? unexpected;
            HttpRequestException? refusal = null;
            try
            { unexpected = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, state.Identity.Secret, token); }
            catch (HttpRequestException error) { unexpected = null; refusal = error; }
            await using var owned = unexpected;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(refusal).IsNotNull();
                await Assert.That(refusal!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
                await Assert.That(refusal!.Message.Contains(state.Identity.Secret, StringComparison.Ordinal)).IsFalse();
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await BlobPrincipalRevokedR44Pages.CurrentAsync(admin, state, token);
    }
}
