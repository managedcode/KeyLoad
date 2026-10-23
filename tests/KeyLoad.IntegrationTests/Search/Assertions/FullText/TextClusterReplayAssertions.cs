using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class TextClusterReplayAssertions
{
    internal const string UpdatedJson = "{\"text\":\"fresh wording\",\"embedding\":\"unit\",\"secret\":\"private-search-canary\",\"owner\":\"owner-a\"}";
    private const string ProjectedFirst = "{\"owner\":\"owner-a\"}";
    private const string ProjectedThird = "{\"owner\":\"owner-b\"}";
    private const string PutDocumentKind = "putDocument";
    private const string PutVectorKind = "putVector";
    private const string DeleteDocumentKind = "deleteDocument";
    private const string English = "fresh";
    private const string RemovedEnglish = "needle";
    private const string RemovedUkrainian = "КИЇВ";
    private const int FirstRankDenominator = 61;
    private const int SecondRankDenominator = 62;
    private const int BothBranches = 2;
    private const int MutationCount = 3;

    internal static async Task ReceiptAsync(CommandRequest command, CommitReceipt receipt)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position > 0).IsTrue();
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(MutationCount);
        MutationReceipt[] expected =
        [
            new(PutDocumentKind, TextClusterScenario.Collection, TextClusterScenario.FirstId, TextClusterScenario.UpdatedRevision),
            new(PutVectorKind, TextClusterScenario.Collection, TextClusterScenario.FirstId, TextClusterScenario.UpdatedRevision),
            new(DeleteDocumentKind, TextClusterScenario.Collection, TextClusterScenario.SecondId, TextClusterScenario.UpdatedRevision)
        ];
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(OrleansSerialization.Serialize(receipt.Mutations[index]).AsSpan()
                .SequenceEqual(OrleansSerialization.Serialize(expected[index]))).IsTrue();
        }
    }

    internal static async Task ReplayAsync(ClusterFixture fixture, string node, KeyLoadClient administrator,
        CommandRequest command, CommitReceipt original, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        var history = await TextClusterReplayHistory.CaptureAsync(administrator, command.Partition, token);
        await using var official = await McpOfficialClient.ConnectAsync(fixture, node, fixture.AdminKey, token);
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await official.CallAsync(
            McpCallerTools.DocumentsCommit, command, token));
        await ReceiptAsync(command, replay.Value);
        await Assert.That(OrleansSerialization.Serialize(replay.Value).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        await TextWaitClusterCutAssertions.OwnerAsync(before, after);
        await TextClusterReplayHistory.RequireAsync(administrator, command.Partition, history, token);
        var first = await PublicReadAssertions.DocumentAsync(await administrator.GetAsync(
            new(command.Partition, TextClusterScenario.Collection, TextClusterScenario.FirstId), token));
        await DocumentAsync(first, command.Partition, TextClusterScenario.FirstId,
            TextClusterScenario.UpdatedRevision, UpdatedJson, false);
        var deleted = await PublicReadAssertions.DocumentAsync(await administrator.GetAsync(
            new(command.Partition, TextClusterScenario.Collection, TextClusterScenario.SecondId), token));
        await Assert.That(deleted).IsNull();
    }

    internal static async Task ResultsAsync(TextClusterScenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        foreach (var text in new[] { RemovedEnglish, RemovedUkrainian })
        {
            await SearchAsync(scenario.Text(text), [], sdk, mcp, token);
        }
        var first = new DocumentResult(new(scenario.Partition, TextClusterScenario.Collection,
            TextClusterScenario.FirstId), TextClusterScenario.UpdatedRevision, ProjectedFirst, true,
            [TextClusterScenario.TextField, TextClusterScenario.VectorField, TextClusterScenario.SecretField]);
        var third = new DocumentResult(new(scenario.Partition, TextClusterScenario.Collection,
            TextClusterScenario.ThirdId), TextClusterScenario.FirstRevision, ProjectedThird, true,
            [TextClusterScenario.TextField, TextClusterScenario.VectorField, TextClusterScenario.SecretField]);
        await SearchAsync(scenario.Text(English), [new(first, 1d / FirstRankDenominator)], sdk, mcp, token);
        await SearchAsync(scenario.Hybrid(English),
            [new(first, BothBranches / (double)FirstRankDenominator), new(third, 1d / SecondRankDenominator)],
            sdk, mcp, token);
    }

    private static async Task SearchAsync(SearchRequest request, RankedDocument[] expected, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var actual = await TextPublicOutcomeAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, token));
        var official = await TextPublicOutcomeAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, request, token));
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue()
            .Because(TextClusterResultDiagnostic.Describe("SDK", actual, expected));
        await Assert.That(JsonDefaults.Serialize(official.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue()
            .Because(TextClusterResultDiagnostic.Describe("MCP", official.Value, expected));
    }

    private static async Task DocumentAsync(DocumentResult? actual, PartitionRef partition, string id,
        long revision, string json, bool redacted)
    {
        await Assert.That(actual).IsNotNull();
        var expected = new DocumentResult(new(partition, TextClusterScenario.Collection, id), revision,
            json, redacted, []);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
