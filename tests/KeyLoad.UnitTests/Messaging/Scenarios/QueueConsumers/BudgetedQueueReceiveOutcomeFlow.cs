using ManagedCode.Communication;
using A = KeyLoad.UnitTests.Features.Messaging.BudgetedQueueReceiveOutcomeAssertions;
using P = KeyLoad.UnitTests.Features.Messaging.BudgetedQueueReceiveOutcomeProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class BudgetedQueueReceiveOutcomeFlow
{
    internal static async Task RunAsync(bool header, CancellationToken token)
    {
        using var fixture = await BudgetedQueueReceiveOutcomeFixture.CreateAsync(header, token);
        await HealthyResolutionAsync(fixture, token);
        await OriginalBudgetAsync(fixture, token);
        await OriginalResultBudgetAsync(fixture, token);
        await fixture.PolicyAsync(repaired: false);
        await A.UnchangedAsync(fixture, async () =>
        {
            Result<object>? published = null;
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            {
                published = fixture.Database.Database.ResolveSubmissionOutcome(fixture.OriginalIntent,
                    fixture.Database.CreateReadWork(token));
                return Task.CompletedTask;
            });
            ArgumentNullException.ThrowIfNull(failure);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(failure.Message).IsEqualTo(P.Refusal);
            await Assert.That(published).IsNull();
        });
        await fixture.PolicyAsync(repaired: true);
        await HealthyResolutionAsync(fixture, token);
        await ContinueAsync(fixture, token);
    }

    private static Task HealthyResolutionAsync(BudgetedQueueReceiveOutcomeFixture fixture, CancellationToken token)
        => A.UnchangedAsync(fixture, async () =>
        {
            var work = fixture.Database.CreateReadWork(token);
            var actual = fixture.Database.Database.ResolveSubmissionOutcome(fixture.OriginalIntent, work);
            await Assert.That(work.ReadBytes).IsGreaterThan(0);
            await A.EqualAsync(fixture.Result, actual);
        });

    private static async Task OriginalBudgetAsync(BudgetedQueueReceiveOutcomeFixture fixture, CancellationToken token)
    {
        await A.UnchangedAsync(fixture, async () =>
        {
            var exhausted = fixture.Database.CreateReadWork(token);
            exhausted.ChargeBytes(fixture.Database.Database.OperationLimitsOptions.Value.MaxQueryReadBytes);
            Result<object>? published = null;
            var denied = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            {
                published = fixture.Database.Database.ResolveSubmissionOutcome(fixture.OriginalIntent, exhausted);
                return Task.CompletedTask;
            });
            ArgumentNullException.ThrowIfNull(denied);
            await Assert.That(denied.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(published).IsNull();
        });
        await A.UnchangedAsync(fixture, async () =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var work = fixture.Database.CreateReadWork(cancellation.Token);
            await cancellation.CancelAsync();
            Result<object>? published = null;
            var cancelled = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            {
                published = fixture.Database.Database.ResolveSubmissionOutcome(fixture.OriginalIntent, work);
                return Task.CompletedTask;
            });
            ArgumentNullException.ThrowIfNull(cancelled);
            await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(published).IsNull();
            await Assert.That(token.IsCancellationRequested).IsFalse();
        });
        await OriginalDeadlineAsync(fixture, token);
        await HealthyResolutionAsync(fixture, token);
    }

    private static async Task OriginalResultBudgetAsync(BudgetedQueueReceiveOutcomeFixture fixture, CancellationToken token)
    {
        await A.UnchangedAsync(fixture, async () =>
        {
            var work = fixture.Database.CreateReadWork(token);
            work.ConstrainResultBytes(P.ConstrainedResultBytes);
            await Assert.That(fixture.Result.Value is ReceiveResult).IsTrue();
            Result<object>? published = null;
            var denied = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            {
                published = fixture.Database.Database.ResolveSubmissionOutcome(fixture.OriginalIntent, work);
                return Task.CompletedTask;
            });
            ArgumentNullException.ThrowIfNull(denied);
            await Assert.That(denied.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(published).IsNull();
            await Assert.That(work.ReadBytes).IsGreaterThan(0);
            await Assert.That(token.IsCancellationRequested).IsFalse();
        });
        await HealthyResolutionAsync(fixture, token);
    }

    private static async Task OriginalDeadlineAsync(BudgetedQueueReceiveOutcomeFixture fixture, CancellationToken token)
    {
        await A.UnchangedAsync(fixture, async () =>
        {
            var clock = fixture.Database.Database.EvaluationClock;
            var work = fixture.Database.CreateReadWork(token);
            work.ConstrainLifetime(clock.GetUtcNow() + P.OriginalDeadline);
            using var deadline = new CancellationTokenSource(P.OriginalDeadline, clock);
            var elapsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = deadline.Token.Register(() => elapsed.TrySetResult());
            await elapsed.Task.WaitAsync(token);
            Result<object>? published = null;
            var expired = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            {
                published = fixture.Database.Database.ResolveSubmissionOutcome(fixture.OriginalIntent, work);
                return Task.CompletedTask;
            });
            ArgumentNullException.ThrowIfNull(expired);
            await Assert.That(expired.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(published).IsNull();
            await Assert.That(token.IsCancellationRequested).IsFalse();
        });
    }

    private static async Task ContinueAsync(BudgetedQueueReceiveOutcomeFixture fixture, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var delivery = fixture.Result.Get<ReceiveResult>().Deliveries.Single();
        var request = new DeliveryCommand(Guid.NewGuid(), fixture.Lane, delivery.Token, DeliveryAction.Ack);
        var index = checked(fixture.Database.ReadCanonicalCut().AppliedPosition + P.Step);
        var operation = fixture.Database.Database.CreateCommand(OperationKind.Delivery, request.CommandId,
            P.Worker, fixture.Database.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request));
        var receipt = fixture.Database.SubmitIssued(operation).Get<CommitReceipt>();
        await A.EqualAsync(new CommitReceipt(request.CommandId, new(fixture.Database.Store.Identity.Incarnation, fixture.Database.Partition.AtomicPartitionId, index), [new(P.AckKind, P.Queue, P.First, P.Acknowledged)], DurabilityProfile.ProcessDurable, 1), receipt);
        var following = fixture.Receive();
        await A.ReceiveAsync(fixture, following.Operation, following.Result, P.Second);
        await A.UnchangedAsync(fixture, async () =>
        {
            await A.EqualAsync(following.Result, fixture.Database.Database.ResolveSubmissionOutcome(following.Operation,
                fixture.Database.CreateReadWork(token)));
        });
    }
}
