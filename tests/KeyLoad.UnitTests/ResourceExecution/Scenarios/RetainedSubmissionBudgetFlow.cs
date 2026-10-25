using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class RetainedSubmissionBudgetFlow
{
    private const string Subject = "retained-budget-writer";
    private const string Collection = "retained-budget-records";
    private const string DocumentId = "committed";
    private const string UncommittedDocumentId = "uncommitted";
    private const string Content = "{\"value\":1}";
    private const long InitialEpoch = 1;
    private const long RevokedEpoch = 2;
    private const long OneByte = 1;
    private const int OneRecord = 1;
    private const int RepeatedReads = 2;
    private const long NoReadBytes = 0;
    private const int NoReadRecords = 0;

    internal static async Task RunAsync()
    {
        using var fixture = new TestDatabase();
        fixture.Configure(Collection, ResourceKind.Collection);
        var subject = new PrincipalRecord(Subject, fixture.Partition.TenantId,
            [new(fixture.Partition.DatabaseId, Collection, Capability.DocumentsWrite)], [])
        {
            PolicyEpoch = InitialEpoch
        };
        await Assert.That(Errors.Code(fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(subject)).Problem).Value).IsNull();
        var original = Issue(fixture, DocumentId);
        var committed = fixture.SubmitIssued(original);
        await Assert.That(Errors.Code(committed.Problem).Value).IsNull();
        await Assert.That(committed.Get<CommitReceipt>().CommandId).IsEqualTo(original.CommandId);
        var work = Work(fixture);
        await HealthyAsync(fixture, original, committed, work);
        await MissingAsync(fixture);
        await CurrentAuthorityAsync(fixture, subject, original, work);
    }

    private static async Task HealthyAsync(TestDatabase fixture, Command<DatabaseCommand> original,
        Result<object> committed, ReadExecutionBudget work)
    {
        var before = RetainedSubmissionBudgetImage.Capture(fixture);
        var first = fixture.Database.ReadRetainedSubmissionOutcome(original, work);
        await RetainedSubmissionBudgetImage.ResultAsync(committed, first);
        var bytes = work.ReadBytes;
        var records = work.ConstrainedRecords;
        await Assert.That(bytes).IsGreaterThan(NoReadBytes);
        await Assert.That(records).IsGreaterThan(NoReadRecords);
        var second = fixture.Database.ReadRetainedSubmissionOutcome(original, work);
        await RetainedSubmissionBudgetImage.ResultAsync(committed, second);
        await Assert.That(work.ReadBytes).IsEqualTo(checked(bytes * RepeatedReads));
        await Assert.That(work.ConstrainedRecords).IsEqualTo(checked(records * RepeatedReads));
        await before.UnchangedAsync(fixture);
    }

    private static async Task MissingAsync(TestDatabase fixture)
    {
        var uncommitted = Issue(fixture, UncommittedDocumentId);
        var before = RetainedSubmissionBudgetImage.Capture(fixture);
        var healthy = Work(fixture);
        await Assert.That(fixture.Database.ReadRetainedSubmissionOutcome(uncommitted, healthy)).IsNull();
        await Assert.That(healthy.ReadBytes).IsGreaterThan(NoReadBytes);
        var tiny = fixture.CreateReadWork(CancellationToken.None);
        tiny.ConstrainWork(OneByte, OneRecord);
        var refused = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadRetainedSubmissionOutcome(uncommitted, tiny));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(fixture.Database.GetDocument("root",
            new(fixture.Partition, Collection, UncommittedDocumentId))).IsNull();
        await before.UnchangedAsync(fixture);
    }

    private static async Task CurrentAuthorityAsync(TestDatabase fixture, PrincipalRecord subject,
        Command<DatabaseCommand> original, ReadExecutionBudget work)
    {
        await Assert.That(Errors.Code(fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(subject with
            {
                Revoked = true,
                PolicyEpoch = RevokedEpoch
            })).Problem).Value).IsNull();
        var before = RetainedSubmissionBudgetImage.Capture(fixture);
        var previousBytes = work.ReadBytes;
        var previousRecords = work.ConstrainedRecords;
        var denied = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadRetainedSubmissionOutcome(original, work));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(work.ReadBytes).IsGreaterThan(previousBytes);
        await Assert.That(work.ConstrainedRecords).IsGreaterThan(previousRecords);
        await before.UnchangedAsync(fixture);
    }

    private static ReadExecutionBudget Work(TestDatabase fixture)
    {
        var work = fixture.CreateReadWork(CancellationToken.None);
        work.ConstrainWork(work.RuntimeMaximumReadBytes, work.MaximumScanRecords);
        return work;
    }

    private static Command<DatabaseCommand> Issue(TestDatabase fixture, string documentId)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, fixture.Partition, [new PutDocument(Collection, documentId, Content)]);
        return fixture.Database.CreateCommand(OperationKind.Batch, id, Subject,
            fixture.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request));
    }
}
