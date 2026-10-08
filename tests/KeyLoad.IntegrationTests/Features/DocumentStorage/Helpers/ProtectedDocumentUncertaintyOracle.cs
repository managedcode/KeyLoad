using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Independent complete caller-visible corpus for the uncertain parent.</summary>
internal static class ProtectedDocumentUncertaintyOracle
{
    private static readonly Guid CommandId = new("bbdf73bb-5d26-43a0-b593-19fbef11bc91");
    private const string ExpectedJson = "{\"title\":\"uncertain Київ\",\"secret\":\"uncertain-private-canary\"}";

    internal static async Task IssuedAsync(CommandRequest actual, PartitionRef partition)
    {
        var expected = new CommandRequest(new("bbdf73bb-5d26-43a0-b593-19fbef11bc91"), partition,
            [new PutDocument("protected-documents", "one", ExpectedJson, 1, ExplicitReplacement: true)]);
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }
    internal static CommitReceipt Receipt(PartitionRef partition, Guid incarnation, long originalPosition) => new(CommandId,
        new(incarnation, partition.AtomicPartitionId, originalPosition, 2),
        [new("putDocument", "protected-documents", "one", 2)], DurabilityProfile.QuorumProcessDurable);

    internal static async Task DocumentAsync(KeyLoadClient sdk, ProtectedDocumentRf3Seed seed, CancellationToken token,
        CommitToken? minimum = null)
    {
        var expected = new DocumentResult(new(seed.Partition, "protected-documents", "one"), 2, ExpectedJson, false, []);
        var actual = minimum is null ? await sdk.GetAsync(expected.Reference, token)
            : await sdk.GetAsync(expected.Reference, minimum, token);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(actual));
    }

    internal static async Task DocumentMcpAsync(McpOfficialClient official, ProtectedDocumentRf3Seed seed,
        CommitToken minimum, CancellationToken token)
    {
        var expected = new DocumentResult(new(seed.Partition, "protected-documents", "one"), 2, ExpectedJson, false, []);
        var actual = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await official.CallAsync(
            "keyload_documents_get", new GetDocumentRequest(expected.Reference, minimum), token));
        await SqlRf3Protocol.EqualAsync<DocumentResult?>(expected, actual.Value);
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, KeyLoadClient target, McpOfficialClient mcp,
        CommandRequest command, CommitReceipt expected, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync("keyload_documents_commit", command, token))).Value);
        var sql = SqlRf3Protocol.Call(command.Partition, "keyload_documents_commit", command, command.CommandId);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, sql, token));
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token))).Applied).IsGreaterThanOrEqualTo(before.Applied);
    }
}
