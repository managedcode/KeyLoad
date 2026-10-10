using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class TargetInboxRecoveryCut
{
    internal static async Task AssertAsync(ZoneTreeStore store, DatabaseEngine database, CommitInboxRequest request, CommitStage stage)
    {
        var document = database.GetDocument(CrashFixtureValues.Principal, new(request.Target.Partition,
            TargetInboxCrashProtocol.Collection, TargetInboxCrashProtocol.Effect));
        var output = database.InspectMessage(CrashFixtureValues.Principal, new(request.Target.Partition, TargetInboxCrashProtocol.Output), TargetInboxCrashProtocol.Message);
        var native = store.Read(view => (Inbox: global::KeyLoad.Storage.StorageRecords.GetRecord<TargetInboxRecord>(view, TargetInboxStorage.Key(request)),
            Capacity: global::KeyLoad.Storage.StorageRecords.GetRecord<TargetInboxCapacity>(view, TargetInboxStorage.CapacityKey(request.Target)),
            Outcome: view.ReadOwnedValue(KeySpace.PartitionOutcome(request.Target.Partition, CrashFixtureValues.Principal, request.CommandId))));
        var committed = document is not null;
        await Assert.That(output is not null).IsEqualTo(committed);
        await Assert.That(native.Inbox is not null).IsEqualTo(committed);
        await Assert.That(native.Capacity is not null).IsEqualTo(committed);
        await Assert.That(native.Outcome is not null).IsEqualTo(committed);
        var source = database.InspectMessage(CrashFixtureValues.Principal, request.Source, request.MessageId);
        await Assert.That(source!.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(source.PayloadJson).IsEqualTo(TargetInboxCrashProtocol.Payload);
        if (stage >= CommitStage.JournalFlushed)
        { await Assert.That(committed).IsTrue(); }
        if (!committed)
        { return; }
        await Assert.That(document!.Revision).IsEqualTo(TargetInboxCrashProtocol.Generation);
        await Assert.That(document.Json).IsEqualTo(TargetInboxCrashProtocol.Payload);
        await Assert.That(output!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(output.PayloadJson).IsEqualTo(TargetInboxCrashProtocol.Payload);
        await Assert.That(native.Capacity!.Count).IsEqualTo(TargetInboxCrashProtocol.Generation);
        var bytes = store.Read(view => view.ReadOwnedValue(TargetInboxStorage.Key(request)))!;
        await Assert.That(native.Capacity.Bytes).IsEqualTo(TargetInboxStorage.Key(request).LongLength + bytes.LongLength);
        await Assert.That(native.Inbox!.EffectsFingerprint).IsEqualTo(JsonData.Fingerprint(request.Effects));
        await Assert.That(native.Inbox.IdentityFingerprint).IsEqualTo(TargetInboxStorage.Identity(request));
    }
}
