using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingHealthyContinuation
{
    private const long OriginalLeaseVersion = 1L;
    private const long HealthyDocumentRevision = 1L;
    private const int InitialAttempt = 1;
    private const int NextCommitPosition = 1;
    private const string DocumentKind = "putDocument";
    private const string AckKind = "Ack";
    private const string EmptyHeaders = "{}";
    private const long MissingDocumentRevision = 0L;

    internal static async Task<(Command<DatabaseCommand> Operation, Result<object> Result)> RunAsync(DatabaseEngine database,
        ZoneTreeStore store, Command<DatabaseCommand> original, List<(Command<DatabaseCommand>, Result<object>)> history, (byte[] Key, byte[] Value)[] originalRows)
    {
        var receive = new ReceiveRequest(Guid.NewGuid(), LocalInboxProcessingCrashContract.OutputLane);
        var receiveOperation = database.CreateCommand(OperationKind.Receive, receive.RequestId, original.UserId!, database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(receive, JsonDefaults.Options));
        var receiveToken = NextToken(database, store);
        var claims = new DeliveryClaims(receive.Lane, LocalInboxProcessingCrashContract.Message,
            original.UserId!, OriginalLeaseVersion, LocalInboxProcessingCrashContract.Generation, receiveToken.Incarnation);
        var expected = new ReceiveResult(receive.RequestId,
            [new(LocalInboxProcessingCrashContract.Message, LocalInboxProcessingCrashContract.Payload, EmptyHeaders,
                database.Sign(claims), OriginalLeaseVersion, new DateTimeOffset(receiveOperation.Timestamp).AddSeconds(receive.LeaseSeconds),
                InitialAttempt, LocalInboxProcessingCrashContract.Generation)], receiveToken);
        var received = database.Apply(receiveOperation);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        await LocalInboxProcessingRecoveryImage.ExactAsync(received.Get<ReceiveResult>(), expected);
        var delivery = await Assert.That(received.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await LocalInboxProcessingRecoveryImage.ExactAsync(database.Verify<DeliveryClaims>(delivery.Token), claims);
        var healthy = new ProcessingRequest(Guid.NewGuid(), LocalInboxProcessingCrashContract.OutputLane, delivery.Token,
            LocalInboxProcessingCrashContract.Handler, LocalInboxProcessingCrashContract.Generation,
            [new PutDocument(LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Healthy, LocalInboxProcessingCrashContract.Payload, MissingDocumentRevision)]);
        var healthyOperation = LocalInboxProcessingRecoveryContinuation.Operation(database, original, healthy);
        healthyOperation.Timestamp = database.EvaluationClock.GetUtcNow().UtcDateTime;
        var healthyToken = NextToken(database, store);
        var completed = database.Apply(healthyOperation);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        await LocalInboxProcessingRecoveryImage.ExactAsync(completed.Get<CommitReceipt>(), new CommitReceipt(healthy.CommandId, healthyToken, [new(DocumentKind, LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Healthy, HealthyDocumentRevision),
                new(AckKind, LocalInboxProcessingCrashContract.Output, LocalInboxProcessingCrashContract.Message,
                    LocalInboxProcessingCrashContract.FinalVersion)], DurabilityProfile.ProcessDurable, 1));
        foreach (var mutation in completed.Get<CommitReceipt>().Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
        await LocalInboxProcessingRecoveryHealthy.RequireAsync(database, store, healthyOperation, completed.Get<CommitReceipt>(), store.Position);
        history.Add((healthyOperation, completed));
        return (receiveOperation, received);
    }

    private static CommitToken NextToken(DatabaseEngine database, ZoneTreeStore store)
    {
        var placement = database.ReadAtomicPartitionPlacement(CrashFixtureValues.Principal,
            LocalInboxProcessingCrashContract.Partition);
        return new(placement.Incarnation, LocalInboxProcessingCrashContract.Partition.AtomicPartitionId,
            checked(store.Position + NextCommitPosition));
    }

}
