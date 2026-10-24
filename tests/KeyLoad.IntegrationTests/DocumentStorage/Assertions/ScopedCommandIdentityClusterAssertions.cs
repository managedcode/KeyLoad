using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DocumentStorage;

/// <summary>Checks the exact public receipts and state for one scoped command identity.</summary>
internal static class ScopedCommandIdentityClusterAssertions
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
        ScopedCommandIdentityClusterScenario scenario, CommandRequest original, string changedJson,
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
        ScopedCommandIdentityClusterScenario scenario, CommitReceipt firstReceipt, CommitReceipt secondReceipt,
        CancellationToken cancellationToken)
    {
        await AssertDocumentAndMessageAsync(sdk, mcp, scenario, true, firstReceipt.Token, cancellationToken);
        await AssertDocumentAndMessageAsync(sdk, mcp, scenario, false, secondReceipt.Token, cancellationToken);
    }

    private static async Task AssertDocumentAndMessageAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ScopedCommandIdentityClusterScenario scenario, bool first, CommitToken minimum, CancellationToken cancellationToken)
    {
        var document = ScopedCommandIdentityClusterOracle.Document(scenario, first);
        await AssertDocumentAsync(sdk, mcp, document, minimum, cancellationToken);
        var request = new InspectMessageRequest(new(document.Reference.Partition, scenario.Queue), "same-message");
        var sdkMessage = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(request, cancellationToken));
        var mcpMessage = await PublicReadAssertions.MessageAsync(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, cancellationToken));
        var expected = ScopedCommandIdentityClusterOracle.Message(first);
        await ScopedCommandIdentityClusterOracle.EqualAsync<MessageInspection?>(expected, sdkMessage);
        await ScopedCommandIdentityClusterOracle.EqualAsync<MessageInspection?>(expected, mcpMessage.Value);
    }

    private static async Task AssertDocumentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        DocumentResult expected, CommitToken minimum, CancellationToken cancellationToken)
    {
        var sdkDocument = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(expected.Reference, minimum, cancellationToken));
        var mcpDocument = await PublicReadAssertions.DocumentAsync(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(expected.Reference, minimum), cancellationToken));
        await ScopedCommandIdentityClusterOracle.EqualAsync<DocumentResult?>(expected, sdkDocument);
        await ScopedCommandIdentityClusterOracle.EqualAsync<DocumentResult?>(expected, mcpDocument.Value);
    }
}
