using System.Text.Json;
using KeyLoad.Core.Authorization;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class SampleRollupLimitTests
{
    [Test]
    public async Task AcSeries023BucketIdentityCapRejectsNewBucketButAllowsCasCorrection()
    {
        using var db = new TestDatabase();
        SampleRollupWholeFlow.Seed(db);
        var engine = Engine(db, UnitExecutionOptions.TimeSeriesExecution().Value with
        {
            MaximumRollupBuckets = 1
        });
        Apply(engine, db.Partition, SampleRollupWholeFlow.Refresh(0)).Get<CommitReceipt>();
        var raw = SampleRollupWholeFlow.Raw(db);
        var rollups = SampleRollupWholeFlow.Rollups(db);
        var request = SampleRollupWholeFlow.Refresh(0) with
        {
            From = SampleRollupWholeFlow.End,
            UntilExclusive = SampleRollupWholeFlow.End.AddMinutes(1)
        };
        var failure = Apply(engine, db.Partition, request);
        await Assert.That(Errors.Code(failure.Problem).Value).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Problem?.Detail).IsEqualTo(SampleRollupProtocol.BucketBudget);
        await Assert.That(SampleRollupWholeFlow.Raw(db)).IsEqualTo(raw);
        await Assert.That(SampleRollupWholeFlow.Rollups(db)).IsEqualTo(rollups);
        Apply(engine, db.Partition, SampleRollupWholeFlow.Refresh(1)).Get<CommitReceipt>();
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 2, 6, 3, 12, 2, 6, 4);
    }
    private static DatabaseEngine Engine(TestDatabase db, TimeSeriesExecutionOptions options)
        => new(db.Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), Options.Create(options), db.Database.EvaluationClock);
    private static Result<object> Apply(DatabaseEngine engine, PartitionRef partition, Mutation mutation)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, partition, [mutation]);
        return engine.ApplyEmbedded(engine.CreateCommand(OperationKind.Batch, id, SampleRollupWholeFlow.Root, default, JsonSerializer.Serialize(command, JsonDefaults.Options)), cancellationToken: default);
    }
}
