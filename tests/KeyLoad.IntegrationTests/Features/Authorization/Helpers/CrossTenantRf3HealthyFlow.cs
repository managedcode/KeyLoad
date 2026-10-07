using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CrossTenantRf3HealthyFlow
{
    internal static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, McpDocumentScenario owned,
        string secret, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), owned.Partition,
            [new PutDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity, CrossTenantRf3WholeFlow.HealthyJson, 1)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt("putDocument", McpDocumentProtocol.Collection, McpDocumentProtocol.Entity, 2));
        var replay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token));
        await Assert.That(JsonDefaults.Serialize(replay)).IsEqualTo(JsonDefaults.Serialize(receipt));
        await Assert.That(JsonDefaults.Serialize(official.Value)).IsEqualTo(JsonDefaults.Serialize(receipt));
        var changed = command with
        {
            Mutations = [new PutDocument(McpDocumentProtocol.Collection,
            McpDocumentProtocol.Entity, McpDocumentProtocol.ConflictingJson, 2)]
        };
        var denied = await sdk.CommitAsync(changed, token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        await Assert.That(denied.Problem.Detail).IsEqualTo("The command ID was already used with different content.");
        var conflict = await mcp.CallAsync(McpCallerTools.DocumentsCommit, changed, token);
        await McpCallerAssertions.ErrorAsync(conflict, ErrorCode.Conflict, dispatched: true);
        await Assert.That(conflict.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo("The command ID was already used with different content.");
        await McpCallerAssertions.DoesNotDiscloseAsync(conflict, secret, McpDocumentProtocol.ConflictValue);
        await CrossTenantRf3StateAssertions.StateAsync(sdk, owned, CrossTenantRf3WholeFlow.HealthyJson, 2, token);
        var read = await McpCallerAssertions.SuccessAsync<DocumentResult>(await mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(owned.Reference), token));
        await Assert.That(JsonDefaults.Serialize(read.Value)).IsEqualTo(JsonDefaults.Serialize(
            new DocumentResult(owned.Reference, 2, CrossTenantRf3WholeFlow.HealthyJson, false, [])));
    }
}
