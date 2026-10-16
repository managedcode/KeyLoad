using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.Authorization.SchemaVisibleProjectionRf3Protocol;
using S = KeyLoad.IntegrationTests.Features.Authorization.SafeProjectorOperationRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionRf3Replay
{
    internal static async Task HealthyAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), history.State.Partition,
            [new PatchDocument(history.State.Collection, S.First, [new(S.StatusPath, PatchKind.Set, StatusJson)], history.Revision)]);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(S.PlacementReadVersion, history.State.Partition), token));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(command, token));
        history.PublicPatch = command;
        history.PublicReceipt = receipt;
        await SafeProjectorOperationRf3Receipts.PatchAsync(receipt, history.State, command.CommandId, history.Revision, placement);
        await NativeSubmissionColdAssertions.ReceiptAsync(receipt,
            new CommitReceipt(command.CommandId, receipt.Token,
                [new(S.PatchKind, history.State.Collection, S.First, S.UpdatedRevision)],
                DurabilityProfile.QuorumProcessDurable));
        await SafeProjectorOperationRf3Images.CaptureAsync(root, history, token);
        await AllAsync(root, reader, history, P.FinalEpoch, token);
    }

    internal static async Task AllAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, long currentEpoch, CancellationToken token)
    {
        await ReceiptAsync(root, root, history, history.State.Seed, history.State.SeedReceipt, token);
        foreach (var refusal in history.Refusals)
        {
            await RefusalAsync(root, reader, history, refusal.Request,
                refusal.Epoch == currentEpoch ? refusal.Detail : ChangedPolicy, token);
        }
        if (history.PublicPatch is { } request && history.PublicReceipt is { } receipt)
        {
            if (currentEpoch == P.FinalEpoch)
            {
                await ReceiptAsync(root, reader, history, request, receipt, token);
            }
            else
            {
                await RefusalAsync(root, reader, history, request, ChangedPolicy, token);
            }
        }
    }

    private static async Task ReceiptAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers issuer, SafeProjectorOperationRf3History history,
        CommandRequest request, CommitReceipt expected, CancellationToken token)
    {
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await issuer.Sdk.CommitAsync(request, token));
            await SqlRf3Protocol.EqualAsync(expected, actual);
            await NativeSubmissionColdAssertions.ReceiptAsync(actual, expected);
        }, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            var reply = await issuer.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, S.Canary);
            await Assert.That(System.Text.Json.JsonSerializer.Serialize(reply)
                .Contains(PrivateReferencesProperty, StringComparison.OrdinalIgnoreCase)).IsFalse();
            var actual = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(reply)).Value;
            await SqlRf3Protocol.EqualAsync(expected, actual);
            await NativeSubmissionColdAssertions.ReceiptAsync(actual, expected);
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

    private const string PrivateReferencesProperty = "compositionReferences";
    private const string ChangedPolicy = "The principal policy changed since this command was evaluated.";
    private const string StatusJson = "\"updated\"";
}
