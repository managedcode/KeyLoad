using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Exercises the real same-index native writer only after all actual Orleans storage owners stopped.</summary>
internal static class PartitionWriterOrleansNativeAppend
{
    internal static Task RefuseAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionWriterOrleansOriginal original, Guid changedIdentity, CancellationToken cancellationToken)
        => PartitionWriterOrleansStores.UseAsync(wave, original.Node, seed, async (_, _, log, database) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = original.Entry.Operation!;
            var changed = original.Entry with { Operation = operation with { Id = changedIdentity } };
            var refusal = Assert.ThrowsExactly<KeyLoadException>(() => log.Append([changed]));
            await Assert.That(refusal.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(refusal.Message).IsEqualTo(PartitionWriterOrleansProtocol.AuthorityDetail);
            await EqualAsync(original.HardState, NativeSerialization.Serialize(log.State));
            await EqualAsync(NativeSerialization.Serialize(original.Entry),
                NativeSerialization.Serialize(log.ReadEntry(original.Entry.Index)));
            await EqualAsync(NativeSerialization.Serialize(original.Result),
                NativeSerialization.Serialize(database.ResolveOutcome(operation)));
        });

    internal static Task AppendAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionWriterOrleansOriginal original, CancellationToken cancellationToken)
        => PartitionWriterOrleansStores.UseAsync(wave, original.Node, seed, async (_, _, log, database) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EqualAsync(original.HardState, NativeSerialization.Serialize(log.State));
            await EqualAsync(NativeSerialization.Serialize(original.Entry),
                NativeSerialization.Serialize(log.ReadEntry(original.Entry.Index)));
            log.Append([original.Entry]);
            await EqualAsync(original.HardState, NativeSerialization.Serialize(log.State));
            await EqualAsync(NativeSerialization.Serialize(original.Entry),
                NativeSerialization.Serialize(log.ReadEntry(original.Entry.Index)));
            await EqualAsync(NativeSerialization.Serialize(original.Result),
                NativeSerialization.Serialize(database.ResolveOutcome(original.Entry.Operation!)));
        });

    private static async Task EqualAsync(byte[] expected, byte[] actual)
        => await Assert.That(actual.SequenceEqual(expected)).IsTrue();
}
