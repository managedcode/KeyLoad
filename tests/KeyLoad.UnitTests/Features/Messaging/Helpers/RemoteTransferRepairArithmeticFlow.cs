using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferRepairArithmeticFlow
{
    internal static async Task RunAsync(bool advancedHistory, bool exchangedGenerations = false)
    {
        using var fixture = new RemoteTransferDatabase(new() { MaxQueueTransferRepairAttempts = RemoteTransferRepairColdProtocol.FullCeiling },
            timeProvider: new RemoteTransferDiscoveryClock());
        var original = await PrepareAsync(fixture, advancedHistory);
        var key = original.IntentKey;
        fixture.Store.Commit((tx, _) =>
        {
            var record = NativeSerialization.Deserialize<RemoteTransferIntentRecord>(original.IntentBytes);
            var state = record.Repairs ?? throw new InvalidOperationException(RemoteTransferRepairColdProtocol.Missing);
            var corrupted = exchangedGenerations
                ? state with
                {
                    AcceptPolicyGeneration = state.CompleteGeneration,
                    CompleteGeneration = state.AcceptPolicyGeneration
                }
                : state with { AcceptPolicyGeneration = long.MaxValue, CompleteGeneration = long.MaxValue };
            tx.PutRecord(key, record with { Repairs = corrupted });
            return true;
        });
        var faultImage = QueueWholeFlowStorage.Bytes((ZoneTreeStore)fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        await RemoteTransferRepairArithmeticAssertions.RefuseAsync(fixture, original, faultImage, position, identity);
        await RemoteTransferRepairArithmeticAssertions.RefuseAsync(fixture, original, faultImage, position, identity);
        fixture.Reopen();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync((ZoneTreeStore)fixture.Store, faultImage, position, identity);
        await RemoteTransferRepairArithmeticAssertions.RefuseAsync(fixture, original, faultImage, position, identity);
        fixture.Store.Commit((tx, _) => { tx.Put(key, original.IntentBytes); return true; });
        await RemoteTransferRepairArithmeticAssertions.RestoredAsync(fixture, original, advancedHistory);
        var completed = RemoteTransferRepairColdNative.Apply(fixture, original.Complete).Get<CommitReceipt>();
        await RemoteTransferRepairArithmeticAssertions.CompletedAsync(fixture, original, completed);
        await RemoteTransferAttemptColdNative.AckAsync(fixture, RemoteTransferRepairColdProtocol.Message);
        await RemoteTransferRepairArithmeticAssertions.SettledAsync(fixture, original, completed, advancedHistory);
        var settled = QueueWholeFlowStorage.Bytes((ZoneTreeStore)fixture.Store);
        position = fixture.Store.Position;
        identity = NativeSerialization.Serialize(fixture.Store.Identity);
        fixture.Reopen();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync((ZoneTreeStore)fixture.Store, settled, position, identity);
        await RemoteTransferRepairArithmeticAssertions.SettledAsync(fixture, original, completed, advancedHistory);
        await RemoteTransferRepairArithmeticAssertions.HealthyAsync(fixture);
        await RemoteTransferRepairArithmeticAssertions.SettledAsync(fixture, original, completed, advancedHistory);
    }

    private static async Task<RemoteTransferRepairArithmeticOriginal> PrepareAsync(RemoteTransferDatabase fixture,
        bool advancedHistory)
    {
        fixture.AddPrincipal(new(RemoteTransferRepairColdProtocol.Subject, RemoteTransferDatabase.TenantId,
            [new(RemoteTransferRepairColdProtocol.Wildcard, RemoteTransferRepairColdProtocol.Wildcard, Capability.All)],
            [RemoteTransferRepairColdProtocol.Wildcard])
        { ClusterAdministrator = true, PolicyEpoch = RemoteTransferRepairColdProtocol.InitialEpoch });
        var transfer = new CreateQueueTransfer(fixture.SourceQueue, Guid.NewGuid(), fixture.DestinationQueue,
            new(fixture.DestinationQueue.Queue, RemoteTransferRepairColdProtocol.Message, RemoteTransferRepairColdProtocol.Payload));
        var create = new CommandRequest(Guid.NewGuid(), fixture.SourcePartition, [transfer]);
        var created = RemoteTransferRepairColdNative.Apply(fixture, create).Get<CommitReceipt>();
        var createBytes = RemoteTransferRepairColdNative.Outcome(fixture, create.CommandId, fixture.SourcePartition);
        var intent = fixture.Database.InspectQueueTransfer(RemoteTransferRepairColdProtocol.Subject, fixture.SourceQueue, transfer.TransferId)
            ?? throw new InvalidOperationException(RemoteTransferRepairColdProtocol.Missing);
        var hint = RemoteTransferRepairColdNative.Hint(fixture);
        RemoteTransferRepairColdNative.Policy(fixture, fixture.DestinationQueue, allow: false);
        var denied = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept),
            fixture.DestinationPartition, [new AcceptQueueTransfer(fixture.DestinationQueue, intent.IntentToken)]);
        var failure = RemoteTransferRepairColdNative.Apply(fixture, denied);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.Json).IsNull();
        await Assert.That(failure.NativeValue).IsNull();
        var deniedBytes = RemoteTransferRepairColdNative.Outcome(fixture, denied.CommandId, fixture.DestinationPartition);
        await RemoteTransferRepairColdAdvance.DeniedWithoutWitnessAsync(fixture, hint, intent.IntentToken, QueueTransferRepairStage.Accept, null);
        RemoteTransferRepairColdNative.Policy(fixture, fixture.DestinationQueue, allow: true);
        if (advancedHistory)
        {
            var advance = await RemoteTransferRepairColdAdvance.ApplyAsync(fixture, hint, intent.IntentToken, QueueTransferRepairStage.Accept, null);
            var receipt = advance.Result.Get<CommitReceipt>();
            await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferRepairColdNative.Apply(fixture, advance.Command).Get<CommitReceipt>(), receipt);
        }
        hint = RemoteTransferRepairColdNative.Hint(fixture);
        var accepted = denied with
        {
            CommandId = advancedHistory
            ? RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept) : Guid.NewGuid()
        };
        var acceptance = RemoteTransferRepairColdNative.Apply(fixture, accepted).Get<CommitReceipt>();
        var proof = fixture.Database.InspectQueueTransferReceipt(RemoteTransferRepairColdProtocol.Subject,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId)
            ?? throw new InvalidOperationException(RemoteTransferRepairColdProtocol.Missing);
        var complete = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Complete),
            fixture.SourcePartition, [new CompleteQueueTransfer(fixture.SourceQueue, transfer.TransferId, proof.ReceiptToken)]);
        var key = RemoteTransferStorage.IntentKey(fixture.SourceQueue, transfer.TransferId);
        var bytes = fixture.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(RemoteTransferRepairColdProtocol.Missing);
        return new(transfer, create, created, createBytes, denied, failure, deniedBytes, accepted, acceptance, intent, proof, complete, key, bytes, QueueWholeFlowStorage.Bytes((ZoneTreeStore)fixture.Store));
    }
}
