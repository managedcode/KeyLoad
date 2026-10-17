using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class LocalProcessingLateR53Replay
{
    internal static async Task RunAsync(LocalProcessingLateR53State state, KeyLoadClient sdk,
        McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        await LocalProcessingLateR53Failures.MixedAsync(state, state.Failed,
            state.OriginalAllowed ? ErrorCode.RevisionConflict : ErrorCode.PermissionDenied,
            state.OriginalAllowed ? LocalProcessingLateR53Protocol.RevisionDetail : LocalProcessingLateR53Protocol.ChangedDetail,
            sdk, mcp, admin, official, token);
        if (state.OriginalAllowed)
        {
            await SuccessAsync(state, state.Success, state.Receipt!, sdk, mcp, admin, official, token);
        }
        else
        {
            await LocalProcessingLateR53Failures.MixedAsync(state, state.Success, ErrorCode.PermissionDenied,
            LocalProcessingLateR53Protocol.ChangedDetail, sdk, mcp, admin, official, token);
        }
        await SuccessAsync(state, state.Success with
        {
            CommandId = Guid.NewGuid()
        }, state.Receipt!, sdk, mcp, admin, official, token);
        await LocalProcessingLateR53Failures.MixedAsync(state, state.Failed with
        {
            CommandId = Guid.NewGuid()
        },
            ErrorCode.Conflict, LocalProcessingLateR53Protocol.EffectDetail, sdk, mcp, admin, official, token);
        if (state.OutputProcessing is { } output)
        {
            await SuccessAsync(state, output, state.OutputReceipt!, sdk, mcp, admin, official, token);
        }
    }

    private static async Task SuccessAsync(LocalProcessingLateR53State state, ProcessingRequest request,
        CommitReceipt expected, KeyLoadClient sdk, McpOfficialClient mcp,
        KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        await InboxProcessingRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CommitProcessingAsync(request, token)), expected);
        await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
        await InboxProcessingRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.MessagesProcess, request, token))).Value, expected);
        await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
    }
}
