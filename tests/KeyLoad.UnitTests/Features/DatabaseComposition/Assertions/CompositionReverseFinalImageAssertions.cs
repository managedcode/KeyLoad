using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.DatabaseComposition;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;
using A = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionBoundReferenceNativeAssertions;
using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionReverseFinalImageData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionReverseFinalImageAssertions
{
    internal static async Task RefusalAsync(OperationResult actual)
    {
        await A.EqualAsync(new OperationResult(null, ErrorCode.NotFound, D.Unavailable), actual);
        await Assert.That(actual.NativeValue).IsNull();
        await Assert.That(actual.Json).IsNull();
    }

    internal static async Task StateAsync(SafeProjectorOperationDatabase owner, CompositionReverseFinalImageState state)
    {
        await OutcomeAsync(owner, state.Original, state.Receipt, state.Bound);
        var retained = owner.Entry(state.Original).Operation!;
        await Assert.That(NativeSerialization.Serialize(retained).SequenceEqual(NativeSerialization.Serialize(state.Original))).IsTrue();
        await Assert.That(retained.Id).IsEqualTo(state.Request.CommandId);
        await Assert.That(retained.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(retained.PrincipalId).IsEqualTo(D.Root);
        await Assert.That(retained.PayloadJson).IsEqualTo(state.Original.PayloadJson);
        await Assert.That(retained.NativePayload.Span.SequenceEqual(state.Original.NativePayload.Span)).IsTrue();
        await Assert.That(NativeOperationFingerprint.Compute(retained)).IsEqualTo(NativeOperationFingerprint.Compute(state.Original));
        var native = NativeSerialization.Deserialize<NativeCommandPayload>(retained.NativePayload.Span);
        await A.EqualAsync(state.Request, NativeSerialization.Deserialize<CommandRequest>(native.Value.Span));
        await OutcomeAsync(owner, state.Seed, state.SeedReceipt, null);
        await Assert.That(owner.Store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(
            view.ReadOwnedValue(KeySpace.Clock.ToArray())!))).IsEqualTo(state.Original.EvaluatedAt);
        await ModelsAsync(owner, state);
    }

    private static async Task OutcomeAsync(SafeProjectorOperationDatabase owner, ReplicatedOperation operation,
        CommitReceipt receipt, EntityRef? start)
    {
        var expected = new StoredOutcome(NativeOperationFingerprint.Compute(operation), owner.Store.Identity.Incarnation,
            1, new(null)
            {
                NativeValue = receipt
            })
        {
            ScopeKind = CommandOutcomeScopeKind.Partition,
            Partition = owner.Source.Partition,
            CompositionAuthority = start is null ? null : new CompositionOutcomeAuthority([start])
        };
        var actual = OutcomeStoreOracle.ReadStored(owner.Store, operation);
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await A.ReceiptAsync(receipt, actual!.Result.Get<CommitReceipt>());
    }

    private static async Task ModelsAsync(SafeProjectorOperationDatabase owner, CompositionReverseFinalImageState state)
    {
        var before = new DocumentRecord(state.Bound, 1, D.Json, new(), state.Seed.EvaluatedAt);
        var tombstone = before with
        {
            Revision = 2,
            Json = "{}",
            Deleted = true,
            UpdatedAt = state.Original.EvaluatedAt
        };
        await A.EqualAsync(tombstone, owner.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(state.Bound))));
        var partition = owner.Source.Partition;
        foreach (var (family, name) in new[] { ("edge", D.Graph), ("adjacency", D.Graph),
            ("message-meta", D.Queue), ("message-body", D.Queue), ("ready", D.Queue), ("queue-counters", D.Queue) })
        {
            var page = owner.Store.Read(view => view.Scan(KeySpace.Partition(family, partition, name), 1));
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Records.IsEmpty).IsTrue();
        }
        var outbox = owner.Store.Read(view => view.Scan(KeySpace.Partition("outbox", partition), 3));
        await Assert.That(outbox.HasMore).IsFalse();
        await Assert.That(outbox.Records.Length).IsEqualTo(2);
        var rows = outbox.Records.Select(row => NativeSerialization.Deserialize<OutboxEntry>(row.Value.Span)).ToArray();
        await A.EqualAsync(new OutboxEntry(1, 0, state.SeedReceipt.Token, state.Seed.EvaluatedAt,
            new PutDocument(D.Collection, D.Id, D.Json, 0), state.SeedReceipt.Mutations.Single(), null, before), rows[0]);
        await A.EqualAsync(new OutboxEntry(2, 0, state.Receipt.Token, state.Original.EvaluatedAt,
            new DeleteDocument(D.Collection, D.Id, 1), state.Receipt.Mutations.Single(), before, tombstone), rows[1]);
        foreach (var entry in rows)
        {
            await Assert.That(entry.Receipt.CompositionReferences.IsEmpty).IsTrue();
        }
    }
}
