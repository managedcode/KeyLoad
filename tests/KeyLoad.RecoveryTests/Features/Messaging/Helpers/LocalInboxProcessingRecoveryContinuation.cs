using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryContinuation
{
    private const long NewGeneration = 2L;
    private const long OriginalLeaseVersion = 1L;
    private const long HealthyDocumentRevision = 1L;
    private const int InitialAttempt = 1;
    private const int PlacementContractVersion = 1;
    private const int NextCommitPosition = 1;
    private const string DocumentKind = "putDocument";
    private const string AckKind = "Ack";
    private const string EmptyHeaders = "{}";
    private const string ConflictDetail = "The inbox input was already completed with different effects.";
    private const string StaleDetail = "The delivery lease is stale.";

    internal static async Task<LocalInboxProcessingRecoveryState> RunAsync(DatabaseEngine database, ZoneTreeStore store,
        ReplicatedOperation original, OperationResult result)
    {
        var history = new List<(ReplicatedOperation, OperationResult)> { (original, result) };
        var request = JsonDefaults.Deserialize<ProcessingRequest>(Encoding.UTF8.GetBytes(original.PayloadJson));
        var duplicate = Operation(original, request with
        {
            CommandId = Guid.NewGuid()
        });
        var before = LocalInboxProcessingRecoveryDomain.Read(store);
        var dedup = database.Apply(duplicate);
        await Assert.That(NativeSerialization.Serialize(dedup)).IsEquivalentTo(NativeSerialization.Serialize(result), CollectionOrdering.Matching);
        await LocalInboxProcessingRecoveryDomain.SameAsync(store, before);
        history.Add((duplicate, dedup));
        var changed = Operation(original, request with
        {
            CommandId = Guid.NewGuid(),
            Effects = [new PutDocument(LocalInboxProcessingCrashContract.Documents, "refused", LocalInboxProcessingCrashContract.Payload)]
        });
        history.Add((changed, await RefuseAsync(database, store, changed, ErrorCode.Conflict, ConflictDetail)));
        var stale = Operation(original, request with
        {
            CommandId = Guid.NewGuid(),
            ExecutionGeneration = NewGeneration,
            Effects = []
        });
        history.Add((stale, await RefuseAsync(database, store, stale, ErrorCode.StaleLease, StaleDetail)));
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
        await LocalInboxProcessingRecoveryImage.ExactAsync(received.Get<ReceiveResult>(), expected);
        var delivery = await Assert.That(received.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await LocalInboxProcessingRecoveryImage.ExactAsync(database.Verify<DeliveryClaims>(delivery.Token), claims);
        var healthy = new ProcessingRequest(Guid.NewGuid(), LocalInboxProcessingCrashContract.OutputLane, delivery.Token,
            LocalInboxProcessingCrashContract.Handler, LocalInboxProcessingCrashContract.Generation,
            [new PutDocument(LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Healthy, LocalInboxProcessingCrashContract.Payload, 0L)]);
        var healthyOperation = Operation(original, healthy) with
        {
            EvaluatedAt = database.EvaluationClock.GetUtcNow()
        };
        var healthyToken = NextToken(database, store);
        var completed = database.Apply(healthyOperation);
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
        foreach (var entry in history)
        {
            await LocalInboxProcessingRecoveryImage.ReplayAsync(database, store, entry.Item1, entry.Item2);
        }
        return new(history.ToArray(), receiveOperation, received);
    }

    private static CommitToken NextToken(DatabaseEngine database, ZoneTreeStore store)
    {
        var placement = database.ReadAtomicPartitionPlacement(CrashFixtureValues.Principal,
            new(PlacementContractVersion, LocalInboxProcessingCrashContract.Partition));
        return new(placement.Incarnation, LocalInboxProcessingCrashContract.Partition.AtomicPartitionId,
            checked(store.Position + NextCommitPosition), placement.PlacementEpoch);
    }

    private static ReplicatedOperation Operation(ReplicatedOperation original, ProcessingRequest request)
        => original with
        {
            Id = request.CommandId,
            PayloadJson = JsonSerializer.Serialize(request, JsonDefaults.Options)
        };

    private static async Task<OperationResult> RefuseAsync(DatabaseEngine database, ZoneTreeStore store,
        ReplicatedOperation operation, ErrorCode code, string detail)
    {
        var before = LocalInboxProcessingRecoveryDomain.Read(store);
        var result = database.Apply(operation);
        await Assert.That(result.Error).IsEqualTo(code);
        await Assert.That(result.SafeDetail).IsEqualTo(detail);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
        await LocalInboxProcessingRecoveryDomain.SameAsync(store, before);
        await LocalInboxProcessingRecoveryImage.ReplayAsync(database, store, operation, result);
        return result;
    }
}
