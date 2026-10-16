using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using A = KeyLoad.UnitTests.Features.ClusterReplication.NativeSubmissionPlanningBudgetAssertions;
using S = KeyLoad.UnitTests.Features.ClusterReplication.NativeQueueRetryPlanningSeed;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class NativeQueueRetryPlanningBudgetFlow
{
    internal static Task RunAsync(bool records, CancellationToken token) => ReplicaSubmissionOutcomeFlow.RunAsync(async owner =>
    {
        var seed = await S.CreateAsync(owner, token);
        await A.UnchangedAsync(owner, async () =>
        {
            var work = A.QueueLimit(owner.Database, seed.Operation, records, token);
            var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.Database.Database.PrepareQueueRetryOperation(seed.Operation, work));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var original = A.Work(owner.Database, cancelled.Token);
            Assert.ThrowsExactly<OperationCanceledException>(() => owner.Database.Database.PrepareQueueRetryOperation(seed.Operation, original));
        });
        ReplicatedOperation? prepared = null;
        await A.UnchangedAsync(owner, async () =>
        {
            var work = A.Work(owner.Database, token);
            prepared = owner.Database.Database.PrepareQueueRetryOperation(seed.Operation, work);
            await Assert.That(work.ReadBytes).IsGreaterThan(0L);
            await RequireDecisionsAsync(owner, seed.Operation, prepared, seed.Lane, seed.Policy);
        });
        var expected = A.Receipt(owner, prepared!.Id, new("Nack", S.Queue, S.Message, 3));
        var actual = owner.Submit(prepared).Get<CommitReceipt>();
        await A.ReceiptAsync(expected, actual);
        await A.UnchangedAsync(owner, async () =>
        {
            await A.ReceiptAsync(expected, owner.Submit(prepared).Get<CommitReceipt>());
            var retained = owner.Database.Database.PrepareQueueRetryOperation(seed.Operation, A.Work(owner.Database, token));
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(seed.Operation), NativeSerialization.Serialize(retained));
            await RetainedChangedAsync(owner, seed.Operation, records, token);
        });
    });

    private static async Task RequireDecisionsAsync(ReplicaSubmissionOutcomeFlow owner, ReplicatedOperation original,
        ReplicatedOperation prepared, QueueLaneRef lane, QueuePolicy policy)
    {
        _ = owner.Database.Database.VerifyOperationAuthority(prepared);
        await Assert.That(NativeOperationFingerprint.Compute(prepared)).IsEqualTo(NativeOperationFingerprint.Compute(original));
        var before = NativeSerialization.Deserialize<NativeCommandPayload>(original.NativePayload.Span);
        var after = NativeSerialization.Deserialize<NativeCommandPayload>(prepared.NativePayload.Span);
        await Assert.That(before.RetryDecisions.IsEmpty).IsTrue();
        await Assert.That(after.Value.Span.SequenceEqual(before.Value.Span)).IsTrue();
        await Assert.That(after.ResourceAdmission.Span.SequenceEqual(before.ResourceAdmission.Span)).IsTrue();
        var choices = NativeSerialization.Deserialize<QueueRetryDecisions>(after.RetryDecisions.Span);
        var choice = await Assert.That(choices.Items).HasSingleItem();
        await Assert.That(choice.DelayMilliseconds).IsGreaterThan(0);
        await Assert.That(choice.DelayMilliseconds).IsLessThanOrEqualTo(policy.RetryBaseMilliseconds);
        var expected = new QueueRetryDecisions(1, original.EvaluatedAt,
            [new(lane, S.Message, 2, 1, 1, 1, ReadOnlyMemory<byte>.Empty,
                SHA256.HashData(NativeSerialization.Serialize(policy)), choice.DelayMilliseconds,
                original.EvaluatedAt.AddMilliseconds(choice.DelayMilliseconds))]);
        await A.EqualAsync(expected, choices);
        var lower = owner.Database.Database.PrepareQueueRetryOperation(original);
        var lowerPayload = NativeSerialization.Deserialize<NativeCommandPayload>(lower.NativePayload.Span);
        await Assert.That(after.RetryDecisions.Span.SequenceEqual(lowerPayload.RetryDecisions.Span)).IsTrue();
    }

    private static async Task RetainedChangedAsync(ReplicaSubmissionOutcomeFlow owner, ReplicatedOperation original,
        bool records, CancellationToken token)
    {
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(original.NativePayload.Span);
        var first = NativeSerialization.Deserialize<DeliveryCommand>(payload.Value.Span);
        var changedRequest = first with
        {
            Action = DeliveryAction.Renew
        };
        var changed = A.Issue(owner, OperationKind.Delivery, changedRequest, original.Id);
        var limited = A.QueueLimit(owner.Database, changed, records, token);
        var budget = Assert.ThrowsExactly<KeyLoadException>(() =>
            owner.Database.Database.PrepareQueueRetryOperation(changed, limited));
        await Assert.That(budget.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var adequate = A.Work(owner.Database, token);
        var conflict = Assert.ThrowsExactly<KeyLoadException>(() =>
            owner.Database.Database.PrepareQueueRetryOperation(changed, adequate));
        await Assert.That(conflict.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(adequate.ReadBytes).IsGreaterThan(0L);
    }

    internal static Task RetainedFailureAsync(CancellationToken token)
        => ReplicaSubmissionOutcomeFlow.RunAsync(async owner =>
    {
        owner.Database.Configure(S.Queue, ResourceKind.WorkQueue);
        var request = new ReceiveRequest(Guid.NewGuid(), new(owner.Database.Partition, S.Queue), MaxMessages: 0);
        var issued = A.Issue(owner, OperationKind.Receive, request, request.RequestId);
        var failed = owner.Submit(issued);
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.Validation);
        var outcome = owner.Outcome(issued);
        await A.UnchangedAsync(owner, async () =>
        {
            var work = A.Work(owner.Database, token);
            var prepared = owner.Database.Database.PrepareQueueRetryOperation(issued, work);
            await Assert.That(work.ReadBytes).IsGreaterThan(0L);
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(issued.NativePayload.ToArray(), prepared.NativePayload.ToArray());
            await ReplicaSubmissionOutcomeFlow.RequireResultAsync(failed, owner.Submit(issued));
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(outcome, owner.Outcome(issued));
        });
    });
}
