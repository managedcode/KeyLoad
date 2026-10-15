using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Replay
{
    private const string ChangedPolicy = "The principal policy changed since this command was evaluated.";
    private const string MissingPatch = "The original public safe projector PATCH command or receipt is absent.";

    internal static async Task AllAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, long currentEpoch, CancellationToken token)
    {
        await SafeProjectorOperationRf3MetadataReplay.AllAsync(root, reader, history, token);
        foreach (var official in new[] { false, true })
        {
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
            {
                var receipt = official ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await root.Mcp.CallAsync(
                    McpCallerTools.DocumentsCommit, history.State.Seed, token))).Value
                    : await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(history.State.Seed, token));
                await SqlRf3Protocol.EqualAsync(history.State.SeedReceipt, receipt);
            }, token);
        }
        foreach (var refusal in history.Refusals)
        {
            var detail = refusal.Epoch == currentEpoch ? refusal.Detail : ChangedPolicy;
            await RefusalAsync(root, reader, history, refusal.Request, detail, token);
        }
        if (history.PublicPatch is not null)
        {
            await PatchAsync(root, reader, history, false, currentEpoch, token);
        }
        if (history.HealthyPatch is not null)
        {
            await PatchAsync(root, reader, history, true, currentEpoch, token);
        }
    }

    internal static async Task PatchAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, bool healthy, long currentEpoch, CancellationToken token)
    {
        var request = (healthy ? history.HealthyPatch : history.PublicPatch) ?? throw new InvalidOperationException(MissingPatch);
        var receipt = (healthy ? history.HealthyReceipt : history.PublicReceipt) ?? throw new InvalidOperationException(MissingPatch);
        var originalEpoch = healthy ? SafeProjectorOperationRf3Protocol.FullWriteEpoch : SafeProjectorOperationRf3Protocol.WithdrawnUseEpoch;
        if (currentEpoch != originalEpoch)
        {
            await RefusalAsync(root, reader, history, request, ChangedPolicy, token);
            return;
        }
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(request, token)));
        }, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token))).Value);
        }, token);
    }

    private static async Task RefusalAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, CommandRequest request, string detail, CancellationToken token)
    {
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationRf3Errors.SdkAsync(await reader.Sdk.CommitAsync(request, token), ErrorCode.PermissionDenied, detail);
        }, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationRf3Errors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token),
                ErrorCode.PermissionDenied, history.State);
        }, token);
    }
}
