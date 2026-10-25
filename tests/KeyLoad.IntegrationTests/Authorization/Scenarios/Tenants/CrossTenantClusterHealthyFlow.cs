using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.DocumentStorage;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class CrossTenantClusterHealthyFlow
{
    internal static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, McpDocumentScenario owned,
        string secret, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), owned.Partition,
            [new PutDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity, CrossTenantClusterWholeFlow.HealthyJson, 1)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt("putDocument", McpDocumentProtocol.Collection, McpDocumentProtocol.Entity, 2));
        var replay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token));
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(official.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
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
        _ = (await McpCallerAssertions.ErrorAsync(conflict, ErrorCode.Conflict, dispatched: true)).Value;
        await Assert.That(conflict.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo("The command ID was already used with different content.");
        await McpCallerAssertions.DoesNotDiscloseAsync(conflict, secret, McpDocumentProtocol.ConflictValue);
        await CrossTenantClusterStateAssertions.StateAsync(sdk, owned, CrossTenantClusterWholeFlow.HealthyJson, 2, token);
        var read = await PublicReadAssertions.RequiredDocumentAsync(await mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(owned.Reference), token));
        await Assert.That(JsonDefaults.Serialize(read.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(
            new DocumentResult(owned.StoredReference, 2, CrossTenantClusterWholeFlow.HealthyJson, false, [])))).IsTrue();
    }
}
