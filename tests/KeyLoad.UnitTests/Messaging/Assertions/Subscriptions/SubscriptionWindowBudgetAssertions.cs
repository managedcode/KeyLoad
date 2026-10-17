using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

using static KeyLoad.UnitTests.Features.Messaging.SubscriptionContinuationProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionWindowBudgetAssertions
{
    private const int PointReadsPlusOneWindowRow = 4;
    private const int FirstPosition = 1;
    private const int FinalPosition = 3;
    private const int Repeats = 2;
    private const string MissingOutcome = "The genuine original claim outcome was overwritten.";
    private const string MissingWindow = "The original window row is missing from its actual read budget.";
    private const string Exceeded = "The subscription window authority read budget is exceeded.";

    internal static async Task RefuseAsync(ZoneTreeStore store, SubscriptionRef group, ReceiveSubscriptionRequest request,
        OperationResult original, SubscriptionWindowBudgetFault fault, CancellationToken token)
    {
        var limits = fault == SubscriptionWindowBudgetFault.AggregateRecords
            ? new DatabaseLimits { MaxScanRecords = PointReadsPlusOneWindowRow }
            : new DatabaseLimits { MaxQueryReadBytes = WindowBytes(store, group) };
        var db = Open(store, limits);
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        for (var repeat = FirstRepeat; repeat < Repeats; repeat++)
        {
            token.ThrowIfCancellationRequested();
            var denied = SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.ReceiveSubscription,
                request, request.RequestId, token);
            await Assert.That(denied.Error).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(denied.SafeDetail).IsEqualTo(Exceeded);
            await Assert.That(denied.Json).IsNull();
            await Assert.That(denied.NativeValue).IsNull();
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            var retained = OutcomeStoreOracle.ReadPartition(store, group.Source.Partition, Subject, request.RequestId)
                ?? throw new InvalidOperationException(MissingOutcome);
            await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(retained, original);
        }
    }

    private static long WindowBytes(ZoneTreeStore store, SubscriptionRef group) => store.Read(view =>
    {
        var bytes = NoBytes;
        for (var position = FirstPosition; position <= FinalPosition; position++)
        {
            var key = SubscriptionCheckpointAuthorityFlow.WindowKey(group, position);
            if (!view.ReadValue(key, _ =>
            {
            }, count => bytes = checked(bytes + count)))
            {
                throw new InvalidOperationException(MissingWindow);
            }
        }
        return bytes;
    });

    private static DatabaseEngine Open(ZoneTreeStore store, DatabaseLimits limits)
        => new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(limits),
            UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(),
            UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(),
            UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(),
            UnitExecutionOptions.MovementCheckpoints(), UnavailablePartitionMovementCheckpointVerifier.Instance);
}
