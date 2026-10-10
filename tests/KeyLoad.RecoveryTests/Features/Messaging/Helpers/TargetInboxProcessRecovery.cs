using System.Text.Json;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class TargetInboxProcessRecovery
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        var operation = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(Path.Combine(root, TargetInboxCrashProtocol.OperationFile), token));
        var request = JsonDefaults.Deserialize<CommitInboxRequest>(System.Text.Encoding.UTF8.GetBytes(operation.PayloadJson));
        var delivery = JsonDefaults.Deserialize<Delivery>(await File.ReadAllBytesAsync(Path.Combine(root, TargetInboxCrashProtocol.DeliveryFile), token));
        CommitInboxResult? original = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = TargetInboxRecoveryDatabase.Open(store);
                await TargetInboxRecoveryCut.AssertAsync(store, database, request, stage);
                original = database.ApplyEmbedded(operation, token).Get<CommitInboxResult>();
                await Assert.That(original.AlreadyProcessed).IsFalse();
                await TargetInboxRecoveryCut.AssertAsync(store, database, request, CommitStage.ApplyCompleted);
                var duplicate = request with { CommandId = Guid.NewGuid() };
                var replay = database.ApplyEmbedded(new(duplicate.CommandId, OperationKind.CommitInbox, CrashFixtureValues.Principal,
                    default, JsonSerializer.Serialize(duplicate, JsonDefaults.Options)), token).Get<CommitInboxResult>();
                await Assert.That(replay.AlreadyProcessed).IsTrue();
                await Assert.That(replay.OriginalEffectsToken).IsEqualTo(original.OriginalEffectsToken);
                var ack = new DeliveryCommand(Guid.NewGuid(), request.Source, delivery.Token, DeliveryAction.Ack);
                _ = database.ApplyEmbedded(new(ack.CommandId, OperationKind.Delivery, CrashFixtureValues.Principal,
                    default, JsonSerializer.Serialize(ack, JsonDefaults.Options)), token).Get<CommitReceipt>();
                await Assert.That(database.InspectMessage(CrashFixtureValues.Principal, request.Source, request.MessageId)!.Metadata.State).IsEqualTo(MessageState.Acked);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        await TargetInboxRecoveryCold.VerifyAsync(root, operation, request, original!, token);
    }
}
