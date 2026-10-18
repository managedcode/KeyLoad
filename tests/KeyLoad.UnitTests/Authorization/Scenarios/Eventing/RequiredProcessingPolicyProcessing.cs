using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class RequiredProcessingPolicyProcessing
{
    internal static async Task<Delivery> ReceiveAsync(RequiredProcessingPolicyFixture fixture, string id, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), fixture.Lane, LeaseSeconds: fixture.Resource.QueuePolicy.MaxLeaseSeconds);
        var position = fixture.Harness.Store.Position + 1L;
        var result = fixture.Apply(OperationKind.Receive, request, request.RequestId, token: token).Get<ReceiveResult>();
        await Assert.That(result.Deliveries.Length).IsEqualTo(1);
        var actual = result.Deliveries[0];
        await RequiredProcessingPolicyAssertions.EqualAsync(new ReceiveResult(request.RequestId,
            [new(id, fixture.Payload, fixture.Headers, actual.Token, 1L, actual.LeaseUntil, 1, 1L)],
            new(fixture.Harness.Store.Identity.Incarnation, fixture.Harness.Partition.AtomicPartitionId, position)), result);
        await Assert.That(actual.Token).IsNotEmpty();
        var evaluatedAt = fixture.Harness.Store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(view.ReadOwnedValue(KeySpace.ClockBytes)!));
        await Assert.That(actual.LeaseUntil).IsEqualTo(evaluatedAt.AddSeconds(fixture.Resource.QueuePolicy.MaxLeaseSeconds));
        await RequiredProcessingPolicyAssertions.EqualAsync(new DeliveryClaims(fixture.Lane, id, RequiredProcessingPolicyFixture.Worker,
            1L, 1L, fixture.Harness.Store.Identity.Incarnation), fixture.Harness.Database.Verify<DeliveryClaims>(actual.Token));
        return actual;
    }
    internal static async Task<(Command<DatabaseCommand> Operation, CommitReceipt Receipt, byte[] Outcome)> CompleteAsync(
        RequiredProcessingPolicyFixture fixture, ProcessingRequest request, string document, CancellationToken token)
    {
        var operation = RequiredProcessingPolicyFixture.Operation(OperationKind.Processing, request, request.CommandId);
        var position = fixture.Harness.Store.Position + 1L;
        var actual = fixture.Harness.Database.ApplyEmbedded(operation, token).Get<CommitReceipt>();
        var expected = new CommitReceipt(request.CommandId, new(fixture.Harness.Store.Identity.Incarnation, fixture.Harness.Partition.AtomicPartitionId, position), [new("putDocument", RequiredProcessingPolicyFixture.Derived, document, 1L), new("Ack", RequiredProcessingPolicyFixture.Queue, document, 3L)], DurabilityProfile.ProcessDurable, 1);
        await RequiredProcessingPolicyAssertions.ReceiptAsync(expected, actual);
        return (operation, expected, RequiredProcessingPolicyAssertions.Outcome(fixture.Harness.Store, fixture.Harness.Partition,
            RequiredProcessingPolicyFixture.Worker, request.CommandId));
    }
    internal static Task CancelledAsync(RequiredProcessingPolicyFixture fixture, ProcessingRequest request)
        => RequiredProcessingPolicyAssertions.ObserveAsync(fixture.Harness.Store, async () =>
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            Result<object>? result = null;
            var failure = Assert.ThrowsExactly<OperationCanceledException>(() => result = fixture.Apply(OperationKind.Processing,
                request, request.CommandId, token: cancellation.Token));
            await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(result).IsNull();
        });
}
