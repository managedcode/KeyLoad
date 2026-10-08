using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Checks the exact public receipts and state for one scoped command identity.</summary>
internal static class ScopedCommandIdentityRf3Assertions
{
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
        ScopedCommandIdentityRf3Scenario scenario, CommitReceipt firstReceipt, CommitReceipt secondReceipt,
        CancellationToken cancellationToken)
    {
        await AssertDocumentAndMessageAsync(sdk, mcp, scenario, true, firstReceipt.Token, cancellationToken);
        await AssertDocumentAndMessageAsync(sdk, mcp, scenario, false, secondReceipt.Token, cancellationToken);
    }

    private static async Task AssertDocumentAndMessageAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ScopedCommandIdentityRf3Scenario scenario, bool first, CommitToken minimum, CancellationToken cancellationToken)
    {
        var document = ScopedCommandIdentityRf3Oracle.Document(scenario, first);
        await AssertDocumentAsync(sdk, mcp, document, minimum, cancellationToken);
        var request = new InspectMessageRequest(new(document.Reference.Partition, scenario.Queue), "same-message");
        var sdkMessage = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var mcpMessage = await McpCallerAssertions.SuccessAsync<MessageInspection?>(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, cancellationToken));
        var expected = ScopedCommandIdentityRf3Oracle.Message(first);
        await ScopedCommandIdentityRf3Oracle.EqualAsync<MessageInspection?>(expected, sdkMessage);
        await ScopedCommandIdentityRf3Oracle.EqualAsync<MessageInspection?>(expected, mcpMessage.Value);
    }

    private static async Task AssertDocumentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        DocumentResult expected, CommitToken minimum, CancellationToken cancellationToken)
    {
        var sdkDocument = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(expected.Reference, minimum, cancellationToken));
        var mcpDocument = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(expected.Reference, minimum), cancellationToken));
        await ScopedCommandIdentityRf3Oracle.EqualAsync<DocumentResult?>(expected, sdkDocument);
        await ScopedCommandIdentityRf3Oracle.EqualAsync<DocumentResult?>(expected, mcpDocument.Value);
    }
}
