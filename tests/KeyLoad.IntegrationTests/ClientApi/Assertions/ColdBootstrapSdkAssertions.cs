using KeyLoad.Client;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class ColdBootstrapSdkAssertions
{
    private const int OneMutation = 1;
    private const int MutationIndex = 0;
    private const string PutKind = "putDocument";
    private const long ZeroPosition = 0;
    internal static async Task ReceiptAsync(CommitReceipt receipt, CommandRequest command)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position).IsGreaterThan(ZeroPosition);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(OneMutation);
        var expected = new MutationReceipt(PutKind, ColdBootstrapSdkFlow.Collection,
            ColdBootstrapSdkFlow.Document, ColdBootstrapSdkFlow.FirstRevision);
        await Assert.That(OrleansSerialization.Serialize(receipt.Mutations[MutationIndex])
            .SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
    }

    internal static async Task RuntimeReceiptAsync(CommitReceipt actual, CommandRequest command,
        long revision, long previousPosition)
    {
        await Assert.That(actual.Token.Position).IsGreaterThan(previousPosition);
        var expected = new CommitReceipt(command.CommandId,
            new(actual.Token.Incarnation, command.Partition.AtomicPartitionId, actual.Token.Position),
            [new(PutKind, ColdBootstrapSdkFlow.Collection, ColdBootstrapSdkFlow.Document, revision)],
            DurabilityProfile.ProcessDurable, 3);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task DocumentAtMinimumAsync(KeyLoadClient sdk, EntityRef reference,
        CommitToken minimum, long revision, string json, CancellationToken token)
    {
        var actual = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(reference, minimum, token));
        var expected = new DocumentResult(reference, revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task DocumentAsync(KeyLoadClient sdk, EntityRef reference, long revision,
        string json, CancellationToken token)
    {
        var actual = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(reference, token));
        await Assert.That(actual is not null).IsTrue();
        var expected = new DocumentResult(reference, revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
