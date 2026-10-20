using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;
namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class TargetInboxProcessRecovery
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        var operation = JsonDefaults.Deserialize<Command<DatabaseCommand>>(await File.ReadAllBytesAsync(Path.Combine(root, TargetInboxCrashProtocol.OperationFile), token));
        var request = OrleansSerialization.Deserialize<CommitInboxRequest>(operation.Value!.Input.Span, ValidationProfile.PublicInputElements);
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
                var duplicate = request with
                {
                    CommandId = Guid.NewGuid()
                };
                var replay = database.ApplyEmbedded(database.CreateCommand(OperationKind.CommitInbox, duplicate.CommandId, CrashFixtureValues.Principal, default, JsonSerializer.Serialize(duplicate, JsonDefaults.Options)), token).Get<CommitInboxResult>();
                await Assert.That(replay.AlreadyProcessed).IsTrue();
                await Assert.That(replay.OriginalEffectsToken).IsEqualTo(original.OriginalEffectsToken);
                var ack = new DeliveryCommand(Guid.NewGuid(), request.Source, delivery.Token, DeliveryAction.Ack);
                _ = database.ApplyEmbedded(database.CreateCommand(OperationKind.Delivery, ack.CommandId, CrashFixtureValues.Principal, default, JsonSerializer.Serialize(ack, JsonDefaults.Options)), token).Get<CommitReceipt>();
                await Assert.That(database.InspectMessage(CrashFixtureValues.Principal, request.Source, request.MessageId)!.Metadata.State).IsEqualTo(MessageState.Acked);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        await TargetInboxRecoveryCold.VerifyAsync(root, operation, request, original!, token);
    }
}
