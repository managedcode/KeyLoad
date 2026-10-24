using System.Text.Json;
using KeyLoad.Core.TimeSeries;

namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class SampleRetentionReadBudgetTests
{
    private const int RawDeleteReadBytes = 16_384;
    private const int RawDeleteTagCharacters = 32_768;

    [Test]
    public async Task AcSeries016RawDeleteScanBudgetRejectsBeforeChangingFloorOrSamples()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var timestamp = SampleAggregateTestData.Start;
        var largeTags = "{\"payload\":\"" + new string('x', RawDeleteTagCharacters) + "\"}";
        SampleAggregateTestData.Append(db, [SampleAggregateTestData.Data(1, timestamp, 1)], largeTags);

        var create = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(
            db.Database.Limits with { MaxQueryReadBytes = RawDeleteReadBytes }),
            UnitExecutionOptions.RuntimeClaimsExecution(), null, db.Database.EvaluationClock);
        var bounded = create(db.Store);
        var command = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series, timestamp.AddTicks(1), 1)]);
        var operation = bounded.CreateCommand(OperationKind.Batch, command.CommandId, SampleAggregateTestData.RootPrincipal,
            bounded.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options));
        var beforeRead = db.Store.GetReadDiagnostics();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => bounded.ApplyEmbedded(operation, default).Get<CommitReceipt>());
        var afterRead = db.Store.GetReadDiagnostics();
        await Assert.That(afterRead.RangeExaminedBytes - beforeRead.RangeExaminedBytes)
            .IsGreaterThanOrEqualTo((long)RawDeleteTagCharacters);
        var key = SampleRetentionStateReader.Key(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series);
        var statusBytes = db.Store.Read(view => view.ReadOwnedValue(key));
        var samples = db.Store.Read(view => view.VisitRange(
            SampleReadKeys.Prefix(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series),
            2, static (_, _) => true));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(statusBytes).IsNull();
        await Assert.That(samples.Records).IsEqualTo(1);
    }
}
