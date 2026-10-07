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
        await Assert.That(receipt.Token.OwnershipEpoch).IsGreaterThan(ZeroPosition);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(OneMutation);
        var expected = new MutationReceipt(PutKind, ColdBootstrapSdkFlow.Collection,
            ColdBootstrapSdkFlow.Document, ColdBootstrapSdkFlow.FirstRevision);
        await Assert.That(NativeSerialization.Serialize(receipt.Mutations[MutationIndex])
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }

    internal static async Task DocumentAsync(KeyLoadClient sdk, EntityRef reference, long revision,
        string json, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(reference, token));
        await Assert.That(actual is not null).IsTrue();
        var expected = new DocumentResult(reference, revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
