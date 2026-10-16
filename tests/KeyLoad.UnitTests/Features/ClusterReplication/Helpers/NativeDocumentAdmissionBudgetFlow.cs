using A = KeyLoad.UnitTests.Features.ClusterReplication.NativeSubmissionPlanningBudgetAssertions;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class NativeDocumentAdmissionBudgetFlow
{
    private const string Document = "planning-budget-document";
    private const string OriginalJson = """{"value":1}""";
    private const string ChangedJson = """{"value":2}""";

    internal static Task RunAsync(bool records, CancellationToken token) => ReplicaSubmissionOutcomeFlow.RunAsync(async owner =>
    {
        var issued = owner.Create(Document, OriginalJson);
        var expected = A.Receipt(owner, issued.Id, new("putDocument", ReplicaSubmissionOutcomeFlow.Resource, Document, 1));
        await A.ReceiptAsync(expected, owner.Submit(issued).Get<CommitReceipt>());
        var outcome = owner.Outcome(issued);
        var changedRequest = new CommandRequest(issued.Id, owner.Database.Partition,
            [new PutDocument(ReplicaSubmissionOutcomeFlow.Resource, Document, ChangedJson, 0)]);
        var changed = owner.Database.Database.CreateNativeOperation(OperationKind.Batch, issued.Id, issued.PrincipalId,
            owner.Database.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(changedRequest));
        await A.UnchangedAsync(owner, async () =>
        {
            var bounded = A.DocumentLimit(owner.Database, records, token);
            var budget = Assert.ThrowsExactly<KeyLoadException>(() =>
                owner.Database.Database.ValidateSubmissionAdmission(changed, changedRequest, bounded));
            await Assert.That(budget.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            var original = A.Work(owner.Database, cancelled.Token);
            Assert.ThrowsExactly<OperationCanceledException>(() =>
                owner.Database.Database.ValidateSubmissionAdmission(changed, changedRequest, original));
            var adequate = A.Work(owner.Database, token);
            var conflict = Assert.ThrowsExactly<KeyLoadException>(() =>
                owner.Database.Database.ValidateSubmissionAdmission(changed, changedRequest, adequate));
            await Assert.That(conflict.Code).IsEqualTo(ErrorCode.Conflict);
            await Assert.That(adequate.ReadBytes).IsGreaterThan(0L);
            var originalRequest = new CommandRequest(issued.Id, owner.Database.Partition,
                [new PutDocument(ReplicaSubmissionOutcomeFlow.Resource, Document, OriginalJson, 0)]);
            var healthy = A.Work(owner.Database, token);
            owner.Database.Database.ValidateSubmissionAdmission(issued, originalRequest, healthy);
            await Assert.That(healthy.ReadBytes).IsGreaterThan(0L);
            await A.ReceiptAsync(expected, owner.Submit(issued).Get<CommitReceipt>());
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(outcome, owner.Outcome(issued));
        });
    });
}
