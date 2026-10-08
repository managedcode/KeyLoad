using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Checks retry authority only through caller-visible receipts and canonical document reads.</summary>
internal sealed record RequestCqrsFaultReceiptOracle(
    PartitionRef Partition,
    string Collection,
    string DocumentId,
    string BeforeJson,
    long BeforeRevision,
    string ExpectedJson)
{
    /// <summary>Reads the same baseline through both real public clients before an interruption scenario.</summary>
    /// <param name="callers">The genuine SDK and official MCP clients.</param>
    /// <param name="partition">The actual private RF3 partition.</param>
    /// <param name="collection">The configured collection.</param>
    /// <param name="documentId">The existing document to update.</param>
    /// <param name="expectedBeforeJson">The exact expected starting document string.</param>
    /// <param name="expectedAfterJson">The exact intended final document string.</param>
    /// <param name="cancellationToken">The bounded scenario token.</param>
    /// <returns>The exact public read baseline for subsequent assertions.</returns>
    internal static async Task<RequestCqrsFaultReceiptOracle> CaptureAsync(RequestCqrsRf3Callers callers,
        PartitionRef partition, string collection, string documentId, string expectedBeforeJson, string expectedAfterJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callers);
        var reference = new EntityRef(partition, collection, documentId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.GetAsync(reference, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        if (sdk is null || mcp.Value is null)
        { throw new InvalidOperationException(RequestCqrsFaultOutcome.MissingBaseline); }
        await Assert.That(sdk.Json).IsEqualTo(expectedBeforeJson);
        await Assert.That(mcp.Value.Json).IsEqualTo(expectedBeforeJson);
        await Assert.That(mcp.Value.Revision).IsEqualTo(sdk.Revision);
        await RequestCqrsFaultReplayContinuation.DocumentAsync(callers, reference, sdk.Revision,
            expectedBeforeJson, cancellationToken).ConfigureAwait(false);
        return new(partition, collection, documentId, expectedBeforeJson, sdk.Revision, expectedAfterJson);
    }

    /// <summary>Retries the identical stable command, compares both full receipts and proves exactly one effect.</summary>
    /// <param name="callers">The genuine SDK and official MCP clients.</param>
    /// <param name="administrator">The existing administrator used only for fresh native placement and cut observations.</param>
    /// <param name="command">The exact original command, retaining its stable command ID and body.</param>
    /// <param name="cancellationToken">The bounded caller token.</param>
    /// <returns>The canonical receipt observed on retry.</returns>
    internal async Task<RequestCqrsFaultRetryResult> RetryAndVerifyAsync(RequestCqrsRf3Callers callers,
        RequestCqrsRf3Callers administrator, CommandRequest command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callers);
        if (command.CommandId == Guid.Empty)
        { throw new InvalidOperationException(RequestCqrsFaultOutcome.EmptyCommandId); }

        var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await RequestCqrsFaultCallers.CommitSdkAsync(
            callers.Sdk, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcpObservation = await RequestCqrsFaultCallers.CommitMcpAsync(callers.Mcp, command, cancellationToken)
            .ConfigureAwait(false);
        var mcpToolResult = mcpObservation.ToolResult
            ?? throw new InvalidOperationException(RequestCqrsFaultOutcome.McpRetryTransportFailure,
                mcpObservation.TransportFailure);
        var mcpReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(mcpToolResult).ConfigureAwait(false);
        await AssertReceiptAsync(sdkReceipt, mcpReceipt.Value, command.CommandId, Collection, DocumentId,
            checked(BeforeRevision + 1)).ConfigureAwait(false);
        await VerifyFinalDocumentAsync(callers, sdkReceipt, cancellationToken).ConfigureAwait(false);
        await RequestCqrsFaultReplayContinuation.VerifyAsync(callers, administrator.Sdk, new EntityRef(Partition, Collection, DocumentId), command, sdkReceipt,
            BeforeJson, checked(BeforeRevision + 1), ExpectedJson, cancellationToken).ConfigureAwait(false);
        return new(sdkReceipt, mcpReceipt.Value, mcpReceipt.RequestId);
    }

    private async Task VerifyFinalDocumentAsync(RequestCqrsRf3Callers callers, CommitReceipt receipt,
        CancellationToken cancellationToken)
    {
        await Assert.That(receipt.Mutations.Length).IsGreaterThan(0);
        var reference = new EntityRef(Partition, Collection, DocumentId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.GetAsync(reference, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        var expectedRevision = checked(BeforeRevision + 1);
        await Assert.That(sdk?.Revision).IsEqualTo(expectedRevision);
        await Assert.That(mcp.Value?.Revision).IsEqualTo(expectedRevision);
        await Assert.That(sdk?.Json).IsEqualTo(ExpectedJson);
        await Assert.That(mcp.Value?.Json).IsEqualTo(ExpectedJson);
        await Assert.That(sdk?.Json).IsNotEqualTo(BeforeJson);
        await RequestCqrsFaultReplayContinuation.DocumentAsync(callers, reference, expectedRevision,
            ExpectedJson, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AssertReceiptAsync(CommitReceipt actual, CommitReceipt expected, Guid commandId,
        string collection, string documentId, long revision)
    {
        await Assert.That(actual.CommandId).IsEqualTo(commandId);
        await Assert.That(expected.CommandId).IsEqualTo(commandId);
        await Assert.That(actual.Mutations.Length).IsEqualTo(1);
        await Assert.That(actual.Mutations[0].Resource).IsEqualTo(collection);
        await Assert.That(actual.Mutations[0].Id).IsEqualTo(documentId);
        await Assert.That(actual.Mutations[0].Revision).IsEqualTo(revision);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}

internal sealed record RequestCqrsFaultRetryResult(CommitReceipt SdkReceipt, CommitReceipt McpReceipt, Guid McpRequestId);
