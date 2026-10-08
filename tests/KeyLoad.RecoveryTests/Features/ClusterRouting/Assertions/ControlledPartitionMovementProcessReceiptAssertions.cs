using System.Text.Json;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Checks complete literal seed outcomes and original immutable native replay bytes.</summary>
internal static class ControlledPartitionMovementProcessReceiptAssertions
{
    internal static async Task<byte[]> SeedAsync(OperationResult result, CommitToken expectedToken,
        DurabilityProfile expectedDurability)
    {
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(ControlledPartitionMovementProcessLiteralCorpus.SeedId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(ControlledPartitionMovementProcessLiteralCorpus.Partition.AtomicPartitionId);
        var expected = ControlledPartitionMovementProcessLiteralCorpus.ExpectedMutations();
        var expectedReceipt = new CommitReceipt(ControlledPartitionMovementProcessLiteralCorpus.SeedId,
            expectedToken, expected, expectedDurability);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(receipt, JsonDefaults.Options).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expectedReceipt, JsonDefaults.Options))).IsTrue();
        await Assert.That(receipt.Mutations.Length).IsEqualTo(expected.Length);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(receipt.Mutations, JsonDefaults.Options).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options))).IsTrue();
        foreach (var mutation in receipt.Mutations)
        {
            await Assert.That(mutation.CompositionReferences.IsDefaultOrEmpty).IsTrue();
        }
        return NativeSerialization.Serialize(receipt);
    }

    internal static async Task ReplayAsync(OperationResult replay, byte[] originalBytes)
    {
        await Assert.That(replay.Error).IsNull();
        await Assert.That(replay.SafeDetail).IsNull();
        await Assert.That(NativeSerialization.Serialize(replay.Get<CommitReceipt>()).SequenceEqual(originalBytes)).IsTrue();
    }
}
