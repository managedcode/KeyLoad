using KeyLoad.Core;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using A = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionBoundReferenceAssertions;
using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionReverseFinalImageData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionReverseFinalImageAssertions
{
    internal static async Task RefusalAsync(Result<object> actual)
    {
        await A.EqualAsync(Result<object>.Fail(Errors.Problem(ErrorCode.NotFound, D.Unavailable)), actual);
        await Assert.That(actual.Value).IsNull();

    }

    internal static async Task StateAsync(SafeProjectorOperationDatabase owner, CompositionReverseFinalImageState state)
    {
        await OutcomeAsync(owner, state.Original, state.Receipt, state.Bound);
        var retained = owner.ReadOutcome(state.Original).PreparedCommand!;
        await Assert.That(OrleansSerialization.Serialize(retained).SequenceEqual(OrleansSerialization.Serialize(state.Original))).IsTrue();
        await Assert.That(retained.CommandId).IsEqualTo(state.Request.CommandId);
        await Assert.That(retained.Value!.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(retained.UserId!).IsEqualTo(D.Root);
        await Assert.That(retained.Value!.Input.Span.SequenceEqual(state.Original.Value!.Input.Span)).IsTrue();
        await Assert.That(DatabaseCommandFingerprint.Compute(retained)).IsEqualTo(DatabaseCommandFingerprint.Compute(state.Original));
        await A.EqualAsync(state.Request, OrleansSerialization.Deserialize<CommandRequest>(retained.Value!.Input.Span));
        await OutcomeAsync(owner, state.Seed, state.SeedReceipt, null);
        await Assert.That(owner.Store.Read(view => OrleansSerialization.Deserialize<DateTimeOffset>(
            view.ReadOwnedValue(KeySpace.Clock.ToArray())!))).IsEqualTo(new DateTimeOffset(state.Original.Timestamp));
        await ModelsAsync(owner, state);
    }

    private static async Task OutcomeAsync(SafeProjectorOperationDatabase owner, Command<DatabaseCommand> operation,
        CommitReceipt receipt, EntityRef? start)
    {
        var actual = OutcomeStoreOracle.ReadStored(owner.Store, operation)
            ?? throw new InvalidOperationException("The original composition outcome is absent.");
        await Assert.That(actual.Fingerprint).IsEqualTo(DatabaseCommandFingerprint.Compute(operation));
        await Assert.That(actual.Incarnation).IsEqualTo(owner.Store.Identity.Incarnation);
        await Assert.That(actual.PolicyEpoch).IsEqualTo(1);
        await Assert.That(actual.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(actual.Partition).IsEqualTo(owner.Source.Partition);
        await Assert.That(actual.OnlineTextAuthority).IsNull();

        await Assert.That(Errors.Code(actual.Result.Problem)).IsNull();
        await Assert.That(actual.Result.Problem?.Detail).IsNull();
        await Assert.That(actual.Result.Value).IsTypeOf<CommitReceipt>();
        await A.ReceiptAsync(receipt, actual.Result.Get<CommitReceipt>());
        if (start is null)
        {
            await Assert.That(actual.CompositionAuthority).IsNull();
        }
        else
        {
            var authority = actual.CompositionAuthority
                ?? throw new InvalidOperationException("The admitted original composition authority is absent.");
            await Assert.That(start.Lifetime).IsNotNull();
            await Assert.That(authority.References.SequenceEqual([start])).IsTrue();
        }
    }

    private static async Task ModelsAsync(SafeProjectorOperationDatabase owner, CompositionReverseFinalImageState state)
    {
        var before = new DocumentRecord(state.Bound, 1, D.Json, new(), new DateTimeOffset(state.Seed.Timestamp));
        var tombstone = before with
        {
            Revision = 2,
            Json = "{}",
            Deleted = true,
            UpdatedAt = new DateTimeOffset(state.Original.Timestamp)
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
        var rows = outbox.Records.Select(row => OrleansSerialization.Deserialize<OutboxEntry>(row.Value.Span)).ToArray();
        await A.EqualAsync(new OutboxEntry(1, 0, state.SeedReceipt.Token, new DateTimeOffset(state.Seed.Timestamp),
            new PutDocument(D.Collection, D.Id, D.Json, 0), state.SeedReceipt.Mutations.Single(), null, before), rows[0]);
        await A.EqualAsync(new OutboxEntry(2, 0, state.Receipt.Token, new DateTimeOffset(state.Original.Timestamp),
            new DeleteDocument(D.Collection, D.Id, 1), state.Receipt.Mutations.Single(), before, tombstone), rows[1]);
        foreach (var entry in rows)
        {
            await Assert.That(entry.Receipt.CompositionReferences.IsEmpty).IsTrue();
        }
    }
}
