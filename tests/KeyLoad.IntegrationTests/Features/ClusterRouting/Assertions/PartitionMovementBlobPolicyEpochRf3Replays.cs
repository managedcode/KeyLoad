using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementBlobPolicyEpochRf3Replays
{
    private const byte ChangedPartByte = 0x43;
    private const string BeginTool = BlobToolNames.BeginUpload;
    private const string PartTool = BlobToolNames.WritePart;
    private const string CompleteTool = BlobToolNames.CompleteUpload;

    internal static async Task RequireDeniedAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken token)
    {
        var originals = seed.BlobOriginals;
        var begun = await seed.Source.BeginBlobUploadAsync(originals.Begin, token).ConfigureAwait(false);
        await RequireSdkDeniedAsync(begun.IsFailed, begun.Value, begun.Problem?.ErrorCode);
        await RequireOtherRoutesDeniedAsync(seed, BeginTool, originals.Begin, token).ConfigureAwait(false);
        foreach (var request in new[] { originals.First, originals.Tail })
        {
            var part = await seed.Source.WriteBlobPartAsync(request, token).ConfigureAwait(false);
            await RequireSdkDeniedAsync(part.IsFailed, part.Value, part.Problem?.ErrorCode);
            await RequireOtherRoutesDeniedAsync(seed, PartTool, request, token).ConfigureAwait(false);
        }
        var completed = await seed.Source.CompleteBlobUploadAsync(originals.Complete, token).ConfigureAwait(false);
        await RequireSdkDeniedAsync(completed.IsFailed, completed.Value, completed.Problem?.ErrorCode);
        await RequireOtherRoutesDeniedAsync(seed, CompleteTool, originals.Complete, token).ConfigureAwait(false);
    }

    internal static async Task RequireChangedOriginalDeniedAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken token)
    {
        var original = seed.BlobOriginals.First;
        var bytes = original.Bytes.ToArray();
        Array.Fill(bytes, ChangedPartByte);
        var changed = original with { Bytes = bytes, Sha256 = BlobIntegrity.PartHash(bytes) };
        var sdk = await seed.Source.WriteBlobPartAsync(changed, token).ConfigureAwait(false);
        await RequireSdkErrorAsync(sdk.IsFailed, sdk.Value, sdk.Problem?.ErrorCode, ErrorCode.Conflict);
        await RequireOtherRoutesErrorAsync(seed, PartTool, changed, ErrorCode.Conflict, token).ConfigureAwait(false);
    }

    internal static async Task RequireQ1PositiveAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var originals = seed.BlobOriginals;
        await RequireQ1Async(seed, BeginTool, originals.Begin, originals.Begun, token).ConfigureAwait(false);
        await RequireQ1Async(seed, PartTool, originals.First, originals.FirstReceipt, token).ConfigureAwait(false);
        await RequireQ1Async(seed, PartTool, originals.Tail, originals.TailReceipt, token).ConfigureAwait(false);
        await RequireQ1Async(seed, CompleteTool, originals.Complete, originals.Completed, token).ConfigureAwait(false);
    }

    private static async Task RequireSdkDeniedAsync(bool failed, object? value, string? code)
    {
        await RequireSdkErrorAsync(failed, value, code, ErrorCode.PermissionDenied);
    }

    private static async Task RequireSdkErrorAsync(bool failed, object? value, string? code, ErrorCode expected)
    {
        await Assert.That(failed).IsTrue();
        if (value is JsonElement json)
        { await Assert.That(json.ValueKind).IsEqualTo(JsonValueKind.Undefined); }
        else
        { await Assert.That(value).IsNull(); }
        await Assert.That(code).IsEqualTo(expected.ToString());
    }

    private static Task RequireOtherRoutesDeniedAsync<T>(PartitionMovementPublicParentRf3Seed seed,
        string tool, T request, CancellationToken token)
        => RequireOtherRoutesErrorAsync(seed, tool, request, ErrorCode.PermissionDenied, token);

    private static async Task RequireOtherRoutesErrorAsync<T>(PartitionMovementPublicParentRf3Seed seed,
        string tool, T request, ErrorCode expected, CancellationToken token)
    {
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(tool, request, token).ConfigureAwait(false),
            expected, dispatched: true);
        var sql = SqlRf3Protocol.Call(seed.Partition, tool, request);
        var q1 = await seed.Source.ExecuteSqlAsync(sql, token).ConfigureAwait(false);
        await RequireSdkErrorAsync(q1.IsFailed, q1.Value, q1.Problem?.ErrorCode, expected);
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(SqlOperationProtocol.ToolName, sql, token)
            .ConfigureAwait(false), expected, dispatched: true);
    }

    private static async Task RequireQ1Async<TRequest, TResult>(PartitionMovementPublicParentRf3Seed seed,
        string tool, TRequest request, TResult expected, CancellationToken token)
    {
        var sql = SqlRf3Protocol.Call(seed.Partition, tool, request);
        var sdk = await SqlRf3Protocol.SdkAsync<TResult>(seed.Source, sql, token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(expected, sdk);
        var official = await SqlRf3Protocol.McpAsync<TResult>(seed.Official, sql, token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(expected, official);
    }
}
