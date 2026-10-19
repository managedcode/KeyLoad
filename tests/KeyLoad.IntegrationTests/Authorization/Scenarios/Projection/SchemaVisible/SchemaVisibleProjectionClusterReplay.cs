using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.Authorization.SchemaVisibleProjectionClusterProtocol;
using S = KeyLoad.IntegrationTests.Features.Authorization.SafeProjectorOperationClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionClusterReplay
{
    internal static async Task HealthyAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), history.State.Partition,
            [new PatchDocument(history.State.Collection, S.First, [new(S.StatusPath, PatchKind.Set, StatusJson)], history.Revision)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(command, token));
        history.PublicPatch = command;
        history.PublicReceipt = receipt;
        await SafeProjectorOperationClusterReceipts.PatchAsync(receipt, history.State, command.CommandId, history.Revision);
        await SubmissionColdAssertions.ReceiptAsync(receipt,
            new CommitReceipt(command.CommandId, receipt.Token,
                [new(S.PatchKind, history.State.Collection, S.First, S.UpdatedRevision)],
                DurabilityProfile.ProcessDurable, 3));
        await SafeProjectorOperationClusterImages.CaptureAsync(root, history, token);
        await AllAsync(root, reader, history, P.FinalEpoch, token);
    }

    internal static async Task AllAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, long currentEpoch, CancellationToken token)
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

    private static async Task ReceiptAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers issuer, SafeProjectorOperationClusterHistory history,
        CommandRequest request, CommitReceipt expected, CancellationToken token)
    {
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await issuer.Sdk.CommitAsync(request, token));
            await SqlClusterProtocol.EqualAsync(expected, actual);
            await SubmissionColdAssertions.ReceiptAsync(actual, expected);
        }, token);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            var reply = await issuer.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, S.Canary);
            await Assert.That(System.Text.Json.JsonSerializer.Serialize(reply)
                .Contains(PrivateReferencesProperty, StringComparison.OrdinalIgnoreCase)).IsFalse();
            var actual = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(reply)).Value;
            await SqlClusterProtocol.EqualAsync(expected, actual);
            await SubmissionColdAssertions.ReceiptAsync(actual, expected);
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

    private const string PrivateReferencesProperty = "compositionReferences";
    private const string ChangedPolicy = "The principal policy changed since this command was evaluated.";
    private const string StatusJson = "\"updated\"";
}
