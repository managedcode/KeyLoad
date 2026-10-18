using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPublishedRowReadFenceAssertions
{
    private const string RangeTool = "keyload_blobs_read_range";
    private const string DetailProperty = "detail";
    private const string Denied = "The persisted principal cannot access this blob.";
    private const string Revision = "The blob head revision changed.";
    private const string Invalid = "The blob request or policy is invalid.";
    private const string SafeDenied = "The authenticated principal is not permitted to perform this operation.";
    private const string SafeDiagnostic = "The database operation could not be completed.";
    private const long FutureRevision = 2L;
    private const long BeyondFourBytes = 5L;

    internal static async Task<OutboxStatus> BaselineAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers creator,
        BlobSupersessionPublicTrial.Version publication, CancellationToken token)
    {
        var baseline = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(publication.Begin.Blob.Partition, token));
        await ObserveAsync(administrator, creator, publication, baseline, token);
        return baseline;
    }

    internal static async Task ObserveAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers creator,
        BlobSupersessionPublicTrial.Version publication, OutboxStatus baseline, CancellationToken token)
    {
        await BlobRowCreatorPublicAssertions.CurrentAsync(creator, publication, token);
        await SqlClusterProtocol.EqualAsync(baseline, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(publication.Begin.Blob.Partition, token)));
        await SqlClusterProtocol.EqualAsync(baseline, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(publication.Begin.Blob.Partition), token))).Value);
    }

    internal static async Task RunAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers other, RequestCqrsClusterCallers creator,
        string credential, string privateValue, BlobSupersessionPublicTrial.Version publication,
        OutboxStatus baseline, CancellationToken token)
    {
        Func<Task> healthy = () => ObserveAsync(administrator, creator, publication, baseline, token);
        foreach (var (request, code, detail) in new[]
        {
            (new BlobReadRequest(publication.Begin.Blob, FutureRevision, 0L, 0), ErrorCode.RevisionConflict, Revision),
            (new BlobReadRequest(publication.Begin.Blob, 1L, BeyondFourBytes, 0), ErrorCode.Validation, Invalid)
        })
        {
            await FailureAsync(creator, request, code, detail, SafeDiagnostic,
                credential, privateValue, healthy, token);
            await FailureAsync(other, request, ErrorCode.PermissionDenied, Denied, SafeDenied,
                credential, privateValue, healthy, token);
        }
    }

    private static async Task FailureAsync(RequestCqrsClusterCallers caller, BlobReadRequest request,
        ErrorCode code, string detail, string safeDetail, string credential, string privateValue,
        Func<Task> healthy, CancellationToken token)
    {
        var sdk = await caller.Sdk.ReadBlobRangeAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), sdk.Problem!);
        await healthy();
        var reply = await caller.Mcp.CallAsync(RangeTool, request, token);
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(DetailProperty).GetString()).IsEqualTo(safeDetail);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, privateValue);
        await healthy();
    }
}
