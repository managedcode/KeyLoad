using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRollupLimitTests
{
    private const int OneBucket = 1;
    private const int ReadByteLimit = 1024;
    private const int LargeTagLength = 2048;
    private const string LargeTagsPrefix = "{\"payload\":\"";
    private const string LargeTagsSuffix = "\"}";
    [Test]
    public async Task AcSeries023BucketIdentityCapRejectsNewBucketButAllowsCasCorrection()
    {
        using var db = new TestDatabase();
        SampleRollupWholeFlow.Seed(db);
        var engine = Engine(db, UnitExecutionOptions.TimeSeriesExecution().Value with { MaximumRollupBuckets = OneBucket });
        Apply(engine, db.Partition, SampleRollupWholeFlow.Refresh(0)).Get<CommitReceipt>();
        var raw = SampleRollupWholeFlow.Raw(db);
        var rollups = SampleRollupWholeFlow.Rollups(db);
        var request = SampleRollupWholeFlow.Refresh(0) with
        { From = SampleRollupWholeFlow.End, UntilExclusive = SampleRollupWholeFlow.End.AddMinutes(1) };
        var failure = Apply(engine, db.Partition, request);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.SafeDetail).IsEqualTo(SampleRollupProtocol.BucketBudget);
        await Assert.That(SampleRollupWholeFlow.Raw(db)).IsEqualTo(raw);
        await Assert.That(SampleRollupWholeFlow.Rollups(db)).IsEqualTo(rollups);
        Apply(engine, db.Partition, SampleRollupWholeFlow.Refresh(1)).Get<CommitReceipt>();
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 2, 6, 3, 12, 2, 6, 4);
    }
    [Test]
    public async Task AcSeries023ChargedNativeRawBytesRejectWholeBucketAndSmallerRangeRemainsHealthy()
    {
        using var db = new TestDatabase(new DatabaseLimits { MaxQueryReadBytes = ReadByteLimit });
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db, [new("large", SampleRollupWholeFlow.Start, 2)],
            LargeTagsPrefix + new string('x', LargeTagLength) + LargeTagsSuffix);
        await SampleRollupWholeFlow.FailedRetry(db, SampleRollupWholeFlow.Refresh(0),
            ErrorCode.BudgetExceeded, SampleRollupProtocol.ByteBudget);
        var from = SampleRollupWholeFlow.End;
        var until = from.AddMinutes(1);
        db.Commit(new RefreshSampleRollup(SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series, from, until, 0));
        var healthy = db.Database.ReadSampleRollup(SampleRollupWholeFlow.Root,
            new(db.Partition, SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series, from, until));
        await Assert.That(healthy).IsEqualTo(new SampleRollupResult(1, new(from, until, 1, null, new(0, 0, null, null, null))));
    }
    private static DatabaseEngine Engine(TestDatabase db, TimeSeriesExecutionOptions options)
        => new(db.Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(),
            UnitExecutionOptions.NativeClaimsExecution(), Options.Create(options), db.Database.EvaluationClock);
    private static OperationResult Apply(DatabaseEngine engine, PartitionRef partition, Mutation mutation)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, partition, [mutation]);
        return engine.ApplyEmbedded(new(id, OperationKind.Batch, SampleRollupWholeFlow.Root, default,
            JsonSerializer.Serialize(command, JsonDefaults.Options)), cancellationToken: default);
    }
}
