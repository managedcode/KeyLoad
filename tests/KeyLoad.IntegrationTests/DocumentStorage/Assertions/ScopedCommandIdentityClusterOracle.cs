using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DocumentStorage;

/// <summary>Independent literal state with authenticated execution placement and original commit cuts.</summary>
internal static class ScopedCommandIdentityClusterOracle
{
    internal static DocumentResult Document(ScopedCommandIdentityClusterScenario scenario, bool first)
        => new(new(first ? scenario.FirstPartition : scenario.SecondPartition, scenario.Collection,
            first ? "partition-a-document" : "partition-b-document")
        { Lifetime = scenario.Lifetime }, 1,
            first ? "{\"value\":\"partition-a-original\"}" : "{\"value\":\"partition-b-original\"}", false, []);

    internal static MessageInspection Message(bool first)
        => new(new("same-message", MessageState.Ready, 0, 1, 1, null, null, null, 0, null, 1, null),
            first ? "{\"value\":\"partition-a-original\"}" : "{\"value\":\"partition-b-original\"}", "{}");

    internal static async Task<(Guid Incarnation, long CutPosition)> BeforeCommandAsync(KeyLoadClient sdk,
        ScopedCommandIdentityClusterScenario scenario, Guid issuedId, CommandRequest command, bool first,
        CancellationToken token)
    {
        var document = Document(scenario, first);
        var expectedCommand = new CommandRequest(issuedId, document.Reference.Partition,
            [new PutDocument(scenario.Collection, document.Reference.Id, document.Json),
                new EnqueueMessage(scenario.Queue, "same-message", document.Json, "{}")]);
        await EqualAsync(expectedCommand, command);
        var node = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        var cut = await ReadCutAsync(sdk, document.Reference.Partition, scenario.Collection, token);
        return (node.Incarnation, cut.CutPosition);
    }

    internal static async Task ReceiptAsync(KeyLoadClient sdk, CommitReceipt actual,
        ScopedCommandIdentityClusterScenario scenario, Guid issuedId, bool first,
        (Guid Incarnation, long CutPosition) before, CancellationToken token)
    {
        var document = Document(scenario, first);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        var cut = await ReadCutAsync(sdk, document.Reference.Partition, scenario.Collection, token);
        await Assert.That(actual.Token.Position).IsGreaterThan(before.CutPosition);
        await Assert.That(actual.Token.Position).IsLessThanOrEqualTo(cut.CutPosition);
        await EqualAsync(new QueryRow[] { new(document.Reference.Id, document.Revision, document.Json, false, []) },
            cut.Rows.ToArray());
        var expected = new CommitReceipt(issuedId,
            new(before.Incarnation, document.Reference.Partition.AtomicPartitionId,
                actual.Token.Position),
            [new("putDocument", scenario.Collection, document.Reference.Id, 1),
                new("enqueue", scenario.Queue, "same-message", 1)], DurabilityProfile.ProcessDurable, 3);
        await EqualAsync(expected, actual);
    }

    private static async Task<QueryPage> ReadCutAsync(KeyLoadClient sdk, PartitionRef partition, string collection,
        CancellationToken token) => await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAstAsync(new(partition,
        new(collection, null, [new("*", "*")], null, [], 1), AllowFullScan: true), token));

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
