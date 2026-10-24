using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Messaging;

internal static class SubscriptionProcessingRecoveryAssertions
{
    internal static async Task VerifyAsync(string path, CommitStage stage, CancellationToken token)
    {
        var original = JsonDefaults.Deserialize<Command<DatabaseCommand>>(await File.ReadAllBytesAsync(
            Path.Combine(path, CrashFixtureValues.ProcessingCommandFile), token));
        using var store = new ZoneTreeStore(new(path), RecoveryExecutionOptions.StorageExecution(),
            RecoveryExecutionOptions.PointCacheExecution());
        var db = TargetInboxRecoveryDatabase.Open(store);
        var group = SubscriptionCrashScenario.Subscription;
        var checkpoint = db.GetSubscription(CrashFixtureValues.Principal, group).Checkpoint;
        await Assert.That(checkpoint is 0 or 1).IsTrue();
        await PrefixAsync(db, store, original, checkpoint == 1, token);
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(checkpoint).IsEqualTo(1L);
        }
        var first = db.Apply(original).Get<SubscriptionProcessingResult>();
        await Assert.That(first.AlreadyProcessed).IsFalse();
        await PrefixAsync(db, store, original, true, token);
        await ReceiptAsync(first, original.CommandId);
        await SubscriptionProcessingRecoveryContinuation.RunAsync(db, store, path, original, first, token);
    }

    private static async Task PrefixAsync(DatabaseEngine db, ZoneTreeStore store, Command<DatabaseCommand> original,
        bool committed, CancellationToken token)
    {
        var group = SubscriptionCrashScenario.Subscription;
        var actual = db.GetSubscription(CrashFixtureValues.Principal, group);
        await Assert.That(actual.Generation).IsEqualTo(1L);
        await Assert.That(actual.OwnershipEpoch).IsEqualTo(1L);
        await Assert.That(actual.IssuedPosition).IsEqualTo(1L);
        await Assert.That(actual.Checkpoint).IsEqualTo(committed ? 1L : 0L);
        await Assert.That(actual.Paused).IsFalse();
        await Assert.That(actual.SafeFailureCode).IsNull();
        var request = OrleansSerialization.Deserialize<SubscriptionProcessingRequest>(original.Value!.Input.Span, ValidationProfile.PublicInputElements);
        var rows = store.Read(view => new
        {
            Window = view.GetRecord<GroupDelivery>(SubscriptionProcessingRecoveryContinuation.Key("subscription-window", group, 1L, 1L)),
            Completion = view.GetRecord<SubscriptionCompletion>(SubscriptionProcessingRecoveryContinuation.Key("subscription-completion", group, 1L, 1L)),
            Inbox = view.GetRecord<InboxRecord>(SubscriptionProcessingRecoveryContinuation.Key("subscription-inbox", group, 1L, request.HandlerScope, request.ExecutionGeneration)),
            Outcome = view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(group.Source.Partition, CrashFixtureValues.Principal, original.CommandId))
        });
        await Assert.That(rows.Window is null).IsEqualTo(committed);
        await Assert.That(rows.Completion is not null).IsEqualTo(committed);
        await Assert.That(rows.Inbox is not null).IsEqualTo(committed);
        await Assert.That(rows.Outcome is not null).IsEqualTo(committed);
        await DocumentAsync(db, committed, token);
        await SubscriptionProcessingRecoveryContinuation.SourceAsync(db, false, token);
        if (committed)
        {
            await Assert.That(rows.Completion).IsEqualTo(new SubscriptionCompletion(1L, 1L, "Acked", 1L));
            var result = rows.Outcome!.Result.Get<SubscriptionProcessingResult>();
            await ReceiptAsync(result, original.CommandId);
            await Assert.That(OrleansSerialization.Serialize(rows.Inbox!.Receipt).AsSpan()
                .SequenceEqual(OrleansSerialization.Serialize(result.Receipt))).IsTrue();
        }
        else
        {
            await Assert.That(rows.Window!.Position).IsEqualTo(1L);
            await Assert.That(rows.Window.State).IsEqualTo(GroupDeliveryState.Leased);
            await Assert.That(rows.Window.Attempts).IsEqualTo(1);
            await Assert.That(rows.Window.LeaseVersion).IsEqualTo(1L);
            await Assert.That(rows.Window.PrincipalId).IsEqualTo(CrashFixtureValues.Principal);
            await Assert.That(rows.Window.LeaseUntil is not null).IsTrue();
        }
    }

    internal static async Task DocumentAsync(DatabaseEngine db, bool committed, CancellationToken token)
    {
        var actual = db.GetDocument(CrashFixtureValues.Principal,
            new(SubscriptionCrashScenario.Partition, CrashFixtureValues.Orders, CrashFixtureValues.Effect), cancellationToken: token);
        await Assert.That(actual is not null).IsEqualTo(committed);
        if (actual is not null)
        {
            await Assert.That(actual.Json).IsEqualTo(CrashFixtureValues.EffectJson);
            await Assert.That(actual.Revision).IsEqualTo(1L);
        }
    }

    internal static async Task ReceiptAsync(SubscriptionProcessingResult result, Guid id)
    {
        await Assert.That(result.Receipt.CommandId).IsEqualTo(id);
        await Assert.That(result.OriginalEffectsToken).IsEqualTo(result.Receipt.Token);
        await Assert.That(result.Receipt.Mutations).IsEquivalentTo(new MutationReceipt[]
        {
            new("putDocument", CrashFixtureValues.Orders, CrashFixtureValues.Effect, 1L),
            new("Ack", CrashFixtureValues.Topic, "1", 1L)
        }, CollectionOrdering.Matching);
    }
}
