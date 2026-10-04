using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Exact comparisons shared by the real SDK/MCP CRUD parity cases.</summary>
internal static class McpDocumentCrudParityAssertions
{
    internal static async Task<CommitReceipt> CommitAndRetryAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest command, bool sdkPrimary, CancellationToken cancellationToken)
    {
        var first = await CommitAsync(sdk, mcp, command, sdkPrimary, cancellationToken);
        var retry = await CommitAsync(sdk, mcp, command, !sdkPrimary, cancellationToken);
        await Assert.That(retry.Token).IsEqualTo(first.Token);
        await Assert.That(JsonDefaults.Serialize(first).AsSpan().SequenceEqual(JsonDefaults.Serialize(retry))).IsTrue();
        return first;
    }

    internal static async Task AssertMutationAsync(CommitReceipt receipt, Guid commandId, string kind, long revision)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0]).IsEqualTo(
            new MutationReceipt(kind, McpDocumentProtocol.Collection, McpDocumentProtocol.Entity, revision));
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
    }

    internal static async Task AssertDocumentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        McpDocumentCrudParityScenario scenario, long? expectedRevision, string? expectedJson,
        CancellationToken cancellationToken)
    {
        var sdkDocument = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.Reference, cancellationToken));
        var mcpDocument = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), cancellationToken));
        if (expectedRevision is null)
        {
            await Assert.That(sdkDocument).IsNull();
            await Assert.That(mcpDocument.Value).IsNull();
            return;
        }

        await Assert.That(sdkDocument).IsNotNull();
        await Assert.That(mcpDocument.Value).IsNotNull();
        var sdkValue = sdkDocument!;
        var mcpValue = mcpDocument.Value!;
        await Assert.That(sdkValue.Revision).IsEqualTo(expectedRevision.Value);
        await Assert.That(mcpValue.Revision).IsEqualTo(expectedRevision.Value);
        await Assert.That(sdkValue.Json).IsEqualTo(expectedJson);
        await Assert.That(mcpValue.Json).IsEqualTo(expectedJson);
        await Assert.That(sdkValue.Reference).IsEqualTo(scenario.Reference);
        await Assert.That(mcpValue.Reference).IsEqualTo(scenario.Reference);
        await Assert.That(JsonDefaults.Serialize(sdkValue).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcpValue))).IsTrue();
    }

    private static async Task<CommitReceipt> CommitAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest command, bool useSdk, CancellationToken cancellationToken)
        => useSdk
            ? await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, cancellationToken))
            : (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, cancellationToken))).Value;
}
