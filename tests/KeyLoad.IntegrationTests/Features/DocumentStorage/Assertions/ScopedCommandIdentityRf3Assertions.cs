using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Checks the exact public receipts and state for one scoped command identity.</summary>
internal static class ScopedCommandIdentityRf3Assertions
{
    internal static async Task AssertReceiptAsync(CommitReceipt receipt, ScopedCommandIdentityRf3Scenario scenario,
        string entity)
    {
        await Assert.That(receipt.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(2);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt("putDocument", scenario.Collection,
            entity, 1));
        await Assert.That(receipt.Mutations[1]).IsEqualTo(new MutationReceipt("enqueue", scenario.Queue,
            "same-message", 1));
    }

    internal static async Task AssertRetryAsync(McpOfficialClient mcp, CommandRequest command,
        CommitReceipt expected, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, cancellationToken));
        await Assert.That(actual.Value.CommandId).IsEqualTo(expected.CommandId);
        await Assert.That(JsonDefaults.Serialize(actual.Value).AsSpan().SequenceEqual(
            JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task AssertRetryAsync(KeyLoadClient sdk, CommandRequest command,
        CommitReceipt expected, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, cancellationToken));
        await Assert.That(actual.CommandId).IsEqualTo(expected.CommandId);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task AssertConflictAsync(McpOfficialClient mcp, KeyLoadClient sdk,
        ScopedCommandIdentityRf3Scenario scenario, CommandRequest original, string changedJson,
        CancellationToken cancellationToken)
    {
        var changed = scenario.CreateCommand(original.CommandId, original.Partition,
            original.Mutations.OfType<PutDocument>().Single().Id, changedJson);
        var mcpResult = await mcp.CallAsync(McpCallerTools.DocumentsCommit, changed, cancellationToken);
        await McpCallerAssertions.ErrorAsync(mcpResult, ErrorCode.Conflict, dispatched: true);
        var sdkResult = await sdk.CommitAsync(changed, cancellationToken);
        await Assert.That(sdkResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
    }

    internal static async Task AssertStateAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ScopedCommandIdentityRf3Scenario scenario, string firstEntity, string firstJson,
        string secondEntity, string secondJson, CancellationToken cancellationToken)
    {
        await AssertDocumentAndMessageAsync(sdk, mcp, scenario, scenario.FirstPartition, firstEntity, firstJson,
            cancellationToken);
        await AssertDocumentAndMessageAsync(sdk, mcp, scenario, scenario.SecondPartition, secondEntity, secondJson,
            cancellationToken);
    }

    private static async Task AssertDocumentAndMessageAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ScopedCommandIdentityRf3Scenario scenario, PartitionRef partition, string entity, string expectedJson,
        CancellationToken cancellationToken)
    {
        await AssertDocumentAsync(sdk, mcp, new(partition, scenario.Collection, entity), expectedJson,
            cancellationToken);
        var lane = new QueueLaneRef(partition, scenario.Queue);
        var request = new InspectMessageRequest(lane, "same-message");
        var sdkMessage = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var mcpMessage = await McpCallerAssertions.SuccessAsync<MessageInspection?>(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, cancellationToken));
        await Assert.That(sdkMessage).IsNotNull();
        await Assert.That(mcpMessage.Value).IsNotNull();
        await Assert.That(sdkMessage!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(mcpMessage.Value!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(sdkMessage.Metadata.ReadySequence).IsEqualTo(1L);
        await Assert.That(mcpMessage.Value.Metadata.ReadySequence).IsEqualTo(1L);
        await Assert.That(sdkMessage.Metadata.StateVersion).IsEqualTo(1L);
        await Assert.That(mcpMessage.Value.Metadata.StateVersion).IsEqualTo(1L);
        await Assert.That(sdkMessage.PayloadJson).IsEqualTo(expectedJson);
        await Assert.That(mcpMessage.Value.PayloadJson).IsEqualTo(expectedJson);
        await Assert.That(JsonDefaults.Serialize(sdkMessage).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcpMessage.Value))).IsTrue();
    }

    private static async Task AssertDocumentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        EntityRef reference, string expectedJson, CancellationToken cancellationToken)
    {
        var sdkDocument = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(reference, cancellationToken));
        var mcpDocument = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), cancellationToken));
        await Assert.That(sdkDocument).IsNotNull();
        await Assert.That(mcpDocument.Value).IsNotNull();
        await Assert.That(sdkDocument!.Reference).IsEqualTo(reference);
        await Assert.That(mcpDocument.Value!.Reference).IsEqualTo(reference);
        await Assert.That(sdkDocument.Json).IsEqualTo(expectedJson);
        await Assert.That(mcpDocument.Value.Json).IsEqualTo(expectedJson);
        await Assert.That(JsonDefaults.Serialize(sdkDocument).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcpDocument.Value))).IsTrue();
    }
}
