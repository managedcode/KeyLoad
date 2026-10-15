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
    private const int PlacementContractVersion = 1;
    private const int NextCommitPosition = 1;
    private const string DocumentKind = "putDocument";
    private const string AckKind = "Ack";
    private const string EmptyHeaders = "{}";
    private const long MissingDocumentRevision = 0L;

    internal static async Task<(ReplicatedOperation Operation, OperationResult Result)> RunAsync(DatabaseEngine database,
        ZoneTreeStore store, ReplicatedOperation original, List<(ReplicatedOperation, OperationResult)> history, (byte[] Key, byte[] Value)[] originalRows)
    {
        var receive = new ReceiveRequest(Guid.NewGuid(), LocalInboxProcessingCrashContract.OutputLane);
        var receiveOperation = original with
        {
            Id = receive.RequestId,
            Kind = OperationKind.Receive,
            EvaluatedAt = database.EvaluationClock.GetUtcNow(),
            PayloadJson = JsonSerializer.Serialize(receive, JsonDefaults.Options)
        };
        var receiveToken = NextToken(database, store);
        var claims = new DeliveryClaims(receive.Lane, LocalInboxProcessingCrashContract.Message,
            original.PrincipalId, OriginalLeaseVersion, LocalInboxProcessingCrashContract.Generation, receiveToken.Incarnation);
        var expected = new ReceiveResult(receive.RequestId,
            [new(LocalInboxProcessingCrashContract.Message, LocalInboxProcessingCrashContract.Payload, EmptyHeaders,
                database.Sign(claims), OriginalLeaseVersion, receiveOperation.EvaluatedAt.AddSeconds(receive.LeaseSeconds),
                InitialAttempt, LocalInboxProcessingCrashContract.Generation)], receiveToken);
        var received = database.Apply(receiveOperation);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        await LocalInboxProcessingRecoveryImage.ExactAsync(received.Get<ReceiveResult>(), expected);
        var delivery = await Assert.That(received.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await LocalInboxProcessingRecoveryImage.ExactAsync(database.Verify<DeliveryClaims>(delivery.Token), claims);
        var healthy = new ProcessingRequest(Guid.NewGuid(), LocalInboxProcessingCrashContract.OutputLane, delivery.Token,
            LocalInboxProcessingCrashContract.Handler, LocalInboxProcessingCrashContract.Generation,
            [new PutDocument(LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Healthy, LocalInboxProcessingCrashContract.Payload, MissingDocumentRevision)]);
        var healthyOperation = LocalInboxProcessingRecoveryContinuation.Operation(original, healthy) with
        {
            EvaluatedAt = database.EvaluationClock.GetUtcNow()
        };
        var healthyToken = NextToken(database, store);
        var completed = database.Apply(healthyOperation);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        await LocalInboxProcessingRecoveryImage.ExactAsync(completed.Get<CommitReceipt>(), new CommitReceipt(healthy.CommandId,
            healthyToken, [new(DocumentKind, LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Healthy, HealthyDocumentRevision),
                new(AckKind, LocalInboxProcessingCrashContract.Output, LocalInboxProcessingCrashContract.Message,
                    LocalInboxProcessingCrashContract.FinalVersion)], DurabilityProfile.ProcessDurable));
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
            new(PlacementContractVersion, LocalInboxProcessingCrashContract.Partition));
        return new(placement.Incarnation, LocalInboxProcessingCrashContract.Partition.AtomicPartitionId,
            checked(store.Position + NextCommitPosition), placement.PlacementEpoch);
    }

}
