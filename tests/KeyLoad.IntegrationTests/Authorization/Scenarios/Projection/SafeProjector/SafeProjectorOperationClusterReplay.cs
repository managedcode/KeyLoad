using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationClusterReplay
{
    private const string ChangedPolicy = "The principal policy changed since this command was evaluated.";
    private const string MissingPatch = "The original public safe projector PATCH command or receipt is absent.";

    internal static async Task AllAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, long currentEpoch, CancellationToken token)
    {
        await SafeProjectorOperationClusterMetadataReplay.AllAsync(root, reader, history, token);
        foreach (var official in new[] { false, true })
        {
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                var receipt = official ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await root.Mcp.CallAsync(
                    McpCallerTools.DocumentsCommit, history.State.Seed, token))).Value
                    : await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(history.State.Seed, token));
                await SqlClusterProtocol.EqualAsync(history.State.SeedReceipt, receipt);
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

    internal static async Task PatchAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, bool healthy, long currentEpoch, CancellationToken token)
    {
        var request = (healthy ? history.HealthyPatch : history.PublicPatch) ?? throw new InvalidOperationException(MissingPatch);
        var receipt = (healthy ? history.HealthyReceipt : history.PublicReceipt) ?? throw new InvalidOperationException(MissingPatch);
        var originalEpoch = healthy ? SafeProjectorOperationClusterProtocol.FullWriteEpoch : SafeProjectorOperationClusterProtocol.WithdrawnUseEpoch;
        if (currentEpoch != originalEpoch)
        {
            await RefusalAsync(root, reader, history, request, ChangedPolicy, token);
            return;
        }
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SqlClusterProtocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(request, token)));
        }, token);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SqlClusterProtocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token))).Value);
        }, token);
    }

    private static async Task RefusalAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, CommandRequest request, string detail, CancellationToken token)
    {
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationClusterErrors.SdkAsync(await reader.Sdk.CommitAsync(request, token), ErrorCode.PermissionDenied, detail);
        }, token);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationClusterErrors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token),
                ErrorCode.PermissionDenied, history.State);
        }, token);
    }
}
