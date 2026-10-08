using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks complete literal seed outcomes and original immutable native replay bytes.</summary>
internal static class ControlledPartitionMovementReceiptAssertions
{
    internal static async Task<byte[]> SeedAsync(OperationResult result, CommitToken expectedToken,
        DurabilityProfile expectedDurability)
    {
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(ControlledPartitionMovementCorpus.SeedId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(ControlledPartitionMovementCorpus.Partition.AtomicPartitionId);
        var expected = ControlledPartitionMovementCorpus.ExpectedMutations();
        var expectedReceipt = new CommitReceipt(ControlledPartitionMovementCorpus.SeedId,
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
