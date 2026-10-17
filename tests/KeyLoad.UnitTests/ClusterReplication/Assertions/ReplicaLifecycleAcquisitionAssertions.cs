using System.Security.Cryptography;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaLifecycleAcquisitionAssertions
{
    internal static ReplicaLifecycleStoreCut Capture(ZoneTreeStore store) => new(store.Position,
        store.Identity with
        {
            SigningKey = ReadOnlyMemory<byte>.Empty
        }, SHA256.HashData(store.Identity.SigningKey.Span),
        QueueWholeFlowStorage.Bytes(store));

    internal static async Task StoreAsync(ZoneTreeStore store, ReplicaLifecycleStoreCut before)
    {
        var current = Capture(store);
        await Assert.That(current.Position).IsEqualTo(before.Position);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(before.Identity),
            NativeSerialization.Serialize(current.Identity));
        await Assert.That(CryptographicOperations.FixedTimeEquals(before.SignerDigest, current.SignerDigest)).IsTrue();
        await Assert.That(current.Rows.SequenceEqual(before.Rows, StringComparer.Ordinal)).IsTrue();
    }

    internal static async Task OriginalAsync(ReplicaLifecycleFixture fixture, ReplicaLifecycleAcquisitionState before,
        CancellationToken token)
    {
        await StoreAsync((ZoneTreeStore)fixture.Database.Store, before.Canonical);
        await StoreAsync(fixture.ReplicaStore, before.Replica);
        await Assert.That(fixture.Configuration.Directory).IsEqualTo(before.Configuration.Directory);
        await Assert.That(fixture.Configuration.LocalId).IsEqualTo(before.Configuration.LocalId);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(before.Applied);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(before.HardState, NativeSerialization.Serialize(fixture.Log.State));
        var entry = fixture.Log.ReadEntry(before.Receipt.Token.Position)!;
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(before.Original),
            NativeSerialization.Serialize(entry.Operation));
        await ReceiptAsync(before.Receipt, fixture.Database.ResolveSubmissionOutcome(before.Original).Get<CommitReceipt>());
        await DocumentAsync(fixture, before.Reference, ReplicaLifecycleAcquisitionFailureFlow.FirstRevision,
            ReplicaLifecycleAcquisitionFailureFlow.FirstJson, before.Original.EvaluatedAt, token);
    }

    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
        => await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(expected), NativeSerialization.Serialize(actual));

    internal static async Task DocumentAsync(ReplicaLifecycleFixture fixture, EntityRef reference,
        long revision, string json, DateTimeOffset evaluatedAt, CancellationToken token)
    {
        var expected = new DocumentResult(reference, revision, json, false, []);
        var actual = fixture.Database.GetDocument(P.Root, reference, cancellationToken: token);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(expected), NativeSerialization.Serialize(actual));
        var record = fixture.Database.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(reference)));
        var expectedRecord = new DocumentRecord(reference, revision, json, new(), evaluatedAt);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(expectedRecord), NativeSerialization.Serialize(record));
    }
}
