using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.Authorization.RowRevisionInferenceRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RowRevisionInferenceRf3Attempts
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RowRevisionInferenceRf3State state, string kind, CancellationToken token)
    {
        foreach (var revision in new[] { P.WrongRevision, P.OriginalRevision, P.RepairRevision })
        {
            foreach (var sdkFirst in new[] { true, false })
            {
                var command = RowRevisionInferenceRf3Requests.Create(state.Partition, kind, P.Hidden, revision);
                await PairAsync(root, reader, state, command, ErrorCode.PermissionDenied, P.RowDenied, sdkFirst, token);
                state.Refused.Add((command, ErrorCode.PermissionDenied, P.RowDenied));
            }
        }
        var stale = RowRevisionInferenceRf3Requests.Create(state.Partition, kind, P.Own, P.WrongRevision);
        await PairAsync(root, reader, state, stale, ErrorCode.RevisionConflict, P.RevisionDenied, true, token);
        state.Refused.Add((stale, ErrorCode.RevisionConflict, P.RevisionDenied));
    }

    private static async Task PairAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RowRevisionInferenceRf3State state, CommandRequest command, ErrorCode code, string detail,
        bool sdkFirst, CancellationToken token)
    {
        if (sdkFirst)
        {
            await SdkAsync(root, reader, state, command, code, detail, token);
            await McpAsync(root, reader, state, command, code, token);
        }
        else
        {
            await McpAsync(root, reader, state, command, code, token);
            await SdkAsync(root, reader, state, command, code, detail, token);
        }
    }

    private static async Task SdkAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RowRevisionInferenceRf3State state, CommandRequest command, ErrorCode code, string detail, CancellationToken token)
    {
        await RowRevisionInferenceRf3Refusals.SdkAsync(await reader.Sdk.CommitAsync(command, token), code, detail, state);
        await RowRevisionInferenceRf3Current.RequireAsync(root, state, token);
    }

    private static async Task McpAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RowRevisionInferenceRf3State state, CommandRequest command, ErrorCode code, CancellationToken token)
    {
        await RowRevisionInferenceRf3Refusals.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token), code, state);
        await RowRevisionInferenceRf3Current.RequireAsync(root, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RowRevisionInferenceRf3State state, CancellationToken token)
    {
        foreach (var item in state.Refused)
        {
            await PairAsync(root, reader, state, item.Command, item.Code, item.Detail, true, token);
        }
    }
}
