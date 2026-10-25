using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal static class QueueEnqueueProcessAssertions
{
    internal static CommitToken Next(DatabaseEngine owner)
        => CrashDatabase.NextCommitToken(owner, QueueEnqueueCrashProtocol.Partition);

    internal static async Task ReceiptAsync(Result<object> result, CommandRequest command, CommitToken expected)
        => await QueueEnqueueProcessValueAssertions.EqualAsync(result.Get<CommitReceipt>(), new CommitReceipt(command.CommandId, expected, command.Mutations.Select(mutation => mutation switch
            {
                PutDocument document => new MutationReceipt(QueueEnqueueCrashProtocol.PutKind, document.Collection, document.Id, QueueEnqueueCrashProtocol.First),
                EnqueueMessage message => new MutationReceipt(QueueEnqueueCrashProtocol.EnqueueKind, message.Queue, message.MessageId, QueueEnqueueCrashProtocol.First),
                _ => throw new InvalidOperationException(QueueEnqueueCrashProtocol.UnexpectedMutation)
            }).ToImmutableArray(), DurabilityProfile.ProcessDurable, 1));

    internal static async Task InitialAsync(DatabaseEngine owner, ZoneTreeStore store, CommandRequest command)
    {
        var messages = command.Mutations.OfType<EnqueueMessage>().ToArray();
        var bodies = messages.Select(message => new MessageBody(message.MessageId, message.PayloadJson,
            message.HeadersJson, message.OrderingKey, JsonData.Fingerprint(message))).ToArray();
        foreach (var pair in messages.Zip(bodies))
        {
            var metadata = new MessageMetadata(pair.First.MessageId, pair.First.NotBefore is null ? MessageState.Ready : MessageState.Scheduled,
                QueueEnqueueCrashProtocol.Initial, QueueEnqueueCrashProtocol.First, pair.First.NotBefore is null ? QueueEnqueueCrashProtocol.First : QueueEnqueueCrashProtocol.Initial, pair.First.NotBefore, null);
            await Assert.That(owner.InspectMessage(CrashFixtureValues.Principal, QueueEnqueueCrashProtocol.Lane, metadata.Id))
                .IsEqualTo(new MessageInspection(metadata, pair.Second.PayloadJson, pair.Second.HeadersJson));
            await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(QueueEnqueueCrashProtocol.BodyFamily,
                command.Partition, QueueEnqueueCrashProtocol.Queue, metadata.Id)))).IsEqualTo(pair.Second);
        }
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueEnqueueCrashProtocol.CountersFamily,
            command.Partition, QueueEnqueueCrashProtocol.Queue))))
            .IsEqualTo(new QueueCounters(QueueEnqueueCrashProtocol.Second, bodies.Sum(body => OrleansSerialization.Serialize(body).LongLength), QueueEnqueueCrashProtocol.Initial, QueueEnqueueCrashProtocol.Initial, QueueEnqueueCrashProtocol.First));
        await ReferencesAsync(store, QueueEnqueueCrashProtocol.ReadyFamily, [Reference(QueueEnqueueCrashProtocol.ReadyFamily, QueueEnqueueCrashProtocol.FirstLong, QueueEnqueueCrashProtocol.Ready)]);
        await ReferencesAsync(store, QueueEnqueueCrashProtocol.ScheduledFamily, [Reference(QueueEnqueueCrashProtocol.ScheduledFamily, messages.Single(message => message.MessageId == QueueEnqueueCrashProtocol.Scheduled).NotBefore!.Value,
            QueueEnqueueCrashProtocol.Scheduled)]);
        await ReferencesAsync(store, QueueEnqueueCrashProtocol.LeaseFamily, []);
        var document = owner.GetDocument(CrashFixtureValues.Principal, new(command.Partition, QueueEnqueueCrashProtocol.Collection, QueueEnqueueCrashProtocol.Ready))!;
        await Assert.That(document.Json).IsEqualTo(QueueEnqueueCrashProtocol.Payload);
        await Assert.That(document.Revision).IsEqualTo(QueueEnqueueCrashProtocol.FirstLong);
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
    }

    internal static async Task ReferencesAsync(ZoneTreeStore store, string family, string[] expected)
    {
        var page = store.Read(view => view.Scan(KeySpace.Partition(family, QueueEnqueueCrashProtocol.Partition,
            QueueEnqueueCrashProtocol.Queue), QueueEnqueueCrashProtocol.ImageRecords));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Select(row => Convert.ToHexString(row.Key.Span) + QueueEnqueueCrashProtocol.ImageSeparator + Convert.ToHexString(row.Value.Span)).ToArray())
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    internal static string Reference(string family, object sequence, string id)
        => Convert.ToHexString(KeySpace.Partition(family, QueueEnqueueCrashProtocol.Partition, QueueEnqueueCrashProtocol.Queue, sequence, id))
            + QueueEnqueueCrashProtocol.ImageSeparator + Convert.ToHexString(OrleansSerialization.Serialize(id));
}
