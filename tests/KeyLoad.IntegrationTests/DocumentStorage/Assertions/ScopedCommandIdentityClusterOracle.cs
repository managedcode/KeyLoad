using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Independent literal state with authenticated native placement and original commit cuts.</summary>
internal static class ScopedCommandIdentityClusterOracle
{
    internal static DocumentResult Document(ScopedCommandIdentityClusterScenario scenario, bool first)
        => new(new(first ? scenario.FirstPartition : scenario.SecondPartition, scenario.Collection,
            first ? "partition-a-document" : "partition-b-document"), 1,
            first ? "{\"value\":\"partition-a-original\"}" : "{\"value\":\"partition-b-original\"}", false, []);

    internal static MessageInspection Message(bool first)
        => new(new("same-message", MessageState.Ready, 0, 1, 1, null, null, null, 0, null, 1, null),
            first ? "{\"value\":\"partition-a-original\"}" : "{\"value\":\"partition-b-original\"}", "{}");

    internal static async Task<(Guid Incarnation, long Applied)> BeforeCommandAsync(KeyLoadClient sdk,
        ScopedCommandIdentityClusterScenario scenario, Guid issuedId, CommandRequest command, bool first,
        CancellationToken token)
    {
        var document = Document(scenario, first);
        var expectedCommand = new CommandRequest(issuedId, document.Reference.Partition,
            [new PutDocument(scenario.Collection, document.Reference.Id, document.Json),
                new EnqueueMessage(scenario.Queue, "same-message", document.Json, "{}")]);
        await EqualAsync(expectedCommand, command);
        var cut = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        return (cut.Incarnation, cut.Applied);
    }

    internal static async Task ReceiptAsync(KeyLoadClient sdk, CommitReceipt actual,
        ScopedCommandIdentityClusterScenario scenario, Guid issuedId, bool first,
        (Guid Incarnation, long Applied) before, CancellationToken token)
    {
        var document = Document(scenario, first);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(actual.Token.Position).IsGreaterThan(before.Applied);
        await Assert.That(actual.Token.Position).IsLessThanOrEqualTo(after.Applied);
        var expected = new CommitReceipt(issuedId,
            new(before.Incarnation, document.Reference.Partition.AtomicPartitionId,
                actual.Token.Position, actual.Token.OwnershipEpoch),
            [new("putDocument", scenario.Collection, document.Reference.Id, 1),
                new("enqueue", scenario.Queue, "same-message", 1)], DurabilityProfile.QuorumProcessDurable);
        await EqualAsync(expected, actual);
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
