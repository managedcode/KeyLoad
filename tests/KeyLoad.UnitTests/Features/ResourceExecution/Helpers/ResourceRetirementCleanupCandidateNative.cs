using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementCleanupNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Exercises the real provider capability; it issues no retirement or D4 authority.</summary>
internal static class ResourceRetirementCleanupCandidateNative
{
    private const string EmptyStore = "candidate-empty";
    private const string BoundaryStore = "candidate-boundary";
    private const string Journal = "commands.wal";
    private const int WalHeaderBytes = 52;
    private const byte FirstKeyByte = 17;
    private const byte SecondKeyByte = 34;
    private const byte InitialByte = 51;
    private const byte FinalByte = 68;
    private const int FinalValueLength = 32;

    internal static async Task RunAsync(TestDatabase fixture)
    {
        await ResourceRetirementCleanupCandidateOwner.RunAsync(fixture, EmptyStore, One, async store =>
        {
            store.Commit((transaction, _) =>
            {
                ((IAtomicCommitCandidateValidation)transaction).ValidateCandidate([]);
                return true;
            });
            await Assert.That(store.Position).IsEqualTo((long)Zero);
            await Assert.That(new FileInfo(Path.Combine(fixture.Directory, EmptyStore, Journal)).Length).IsEqualTo((long)Zero);
        });
        byte[] key = [FirstKeyByte];
        byte[] other = [SecondKeyByte];
        byte[] initial = [InitialByte];
        var final = Enumerable.Repeat(FinalByte, FinalValueLength).ToArray();
        StorageMutation[] proposed = [new(key, final), new(other, null)];
        var payload = ZoneTreeJournalCodec.Serialize(proposed, int.MaxValue);
        await ResourceRetirementCleanupCandidateOwner.RunAsync(fixture, BoundaryStore, payload.Length,
            store => BoundaryAsync(fixture, store, key, other, initial, proposed, payload));
    }

    private static async Task BoundaryAsync(TestDatabase fixture, ZoneTreeStore store, byte[] key,
        byte[] other, byte[] initial, StorageMutation[] proposed, byte[] payload)
    {
        KeyLoadException? raw = null;
        KeyLoadException? binary = null;
        byte[]? staged = null;
        byte[]? unstaged = null;
        long during = -One;
        var path = Path.Combine(fixture.Directory, BoundaryStore, Journal);
        store.Commit((transaction, _) =>
        {
            transaction.Put(key, initial);
            transaction.ValidateCommit();
            var capability = (IAtomicCommitCandidateValidation)transaction;
            capability.ValidateCandidate(proposed);
            raw = Assert.ThrowsExactly<KeyLoadException>(() => capability.ValidateCandidate([new(key, new byte[payload.Length])]));
            binary = Assert.ThrowsExactly<KeyLoadException>(() => capability.ValidateCandidate(
                [new(key, new byte[payload.Length - key.Length])]));
            capability.ValidateCandidate(proposed);
            transaction.ValidateCommit();
            staged = transaction.ReadOwnedValue(key);
            unstaged = transaction.ReadOwnedValue(other);
            during = new FileInfo(path).Length;
            return true;
        });
        await Assert.That(raw!.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(binary!.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(binary.Message).IsEqualTo(ZoneTreePersistenceFormat.EncodedTransactionFrameLimitExceeded);
        await Assert.That(during).IsEqualTo((long)Zero);
        await Assert.That(staged).IsEquivalentTo(initial, CollectionOrdering.Matching);
        await Assert.That(unstaged).IsNull();
        var expected = ZoneTreeJournalCodec.Serialize([new(key, initial)], int.MaxValue);
        await Assert.That(File.ReadAllBytes(path).AsSpan(WalHeaderBytes).ToArray()).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo((long)One);
        var position = store.Position;
        var image = ResourceRetirementCleanupNativeImages.Read(store);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => store.Commit<bool>((transaction, _) =>
        {
            ((IAtomicCommitCandidateValidation)transaction).ValidateCandidate(proposed);
            foreach (var row in proposed)
            {
                transaction.Delete(row.Key.ToArray());
            }

            throw new InvalidOperationException(Rollback);
        }));
        await Assert.That(error!.Message).IsEqualTo(Rollback);
        await ResourceRetirementCleanupNativeImages.SameAsync(store, image, position);
        Healthy(store, proposed);
        image[Convert.ToHexString(key)] = Convert.ToHexString(proposed[FirstIndex].Value!.Value.Span);
        await ResourceRetirementCleanupNativeImages.SameAsync(store, image, position + One);
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))).IsEquivalentTo(proposed[FirstIndex].Value!.Value.ToArray(), CollectionOrdering.Matching);
        await Assert.That(store.Read(view => view.ReadOwnedValue(other))).IsNull();
        var bytes = File.ReadAllBytes(path);
        await Assert.That(bytes.AsSpan(expected.Length + WalHeaderBytes + WalHeaderBytes).ToArray())
            .IsEquivalentTo(payload, CollectionOrdering.Matching);
    }

    private static void Healthy(ZoneTreeStore store, StorageMutation[] proposed)
        => store.Commit((transaction, _) =>
        {
            foreach (var row in proposed)
            {
                if (row.Value is { } value)
                {
                    transaction.Put(row.Key.ToArray(), value.ToArray());
                }
                else
                {
                    transaction.Delete(row.Key.ToArray());
                }
            }
            ((IAtomicCommitCandidateValidation)transaction).ValidateCandidate([]);
            transaction.ValidateCommit();
            return true;
        });
}
