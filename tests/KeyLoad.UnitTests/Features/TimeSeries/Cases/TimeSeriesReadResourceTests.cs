using System.Collections.Immutable;
using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class TimeSeriesReadResourceTests
{
    private const string Principal = "root";
    private const string SetName = "metrics";
    private const string Series = "cpu";
    private const string SelectedId = "selected";
    private const string LaterIdPrefix = "later-";
    private const string TagsPrefix = "{\"padding\":\"";
    private const string TagsSuffix = "\"}";
    private const string MinimumId = "minimum";
    private const string MaximumFirstId = "maximum-first";
    private const string MaximumSecondId = "maximum-second";
    private const string OffsetId = "offset";
    private const string Reader = "series-reader";
    private const string ProtectedPath = "/secret";
    private const string ProtectedTags = "{\"secret\":\"private-value\",\"public\":1}";
    private const string ProtectedValue = "private-value";
    private const string PrivateClassification = "private";
    private const int LaterSamples = 12;
    private const int LaterTagCharacters = 524_288;
    private const long AllocationAllowance = 131_072;
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AcMp005NarrowRangeSkipsLargeLaterValuesBeforeCopyingOrDecoding()
    {
        using var db = new TestDatabase();
        db.Configure(SetName, ResourceKind.TimeSeries);
        db.Commit(new AppendSamples(SetName, Series, [new(SelectedId, Timestamp, 1)]));
        var tags = TagsPrefix + new string('x', LaterTagCharacters) + TagsSuffix;
        var later = Enumerable.Range(1, LaterSamples)
            .Select(index => new SampleData(LaterIdPrefix + index, Timestamp.AddTicks(index), index)).ToImmutableArray();
        db.Commit(new AppendSamples(SetName, Series, later, tags));
        var bounded = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxQueryReadBytes = 4_096 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        bounded.ReadSamples(Principal, db.Partition, SetName, Series, Timestamp, Timestamp);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = bounded.ReadSamples(Principal, db.Partition, SetName, Series, Timestamp, Timestamp);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(result.Select(record => record.Sample.EventId)).IsEquivalentTo(new[] { SelectedId }, CollectionOrdering.Matching);
        await Assert.That(allocated).IsLessThan(AllocationAllowance);
    }

    [Test]
    public async Task AcMp005InclusiveUtcBoundsHandleEqualTimestampsAndDateLimits()
    {
        using var db = new TestDatabase();
        db.Configure(SetName, ResourceKind.TimeSeries);
        var offset = new DateTimeOffset(DateTime.MaxValue, TimeSpan.FromHours(1));
        db.Commit(new AppendSamples(SetName, Series,
        [
            new(MaximumFirstId, DateTimeOffset.MaxValue, 1),
            new(MinimumId, DateTimeOffset.MinValue, 2),
            new(MaximumSecondId, DateTimeOffset.MaxValue, 3),
            new(OffsetId, offset, 4)
        ]));

        var minimum = db.Database.ReadSamples(Principal, db.Partition, SetName, Series, DateTimeOffset.MinValue, DateTimeOffset.MinValue);
        var maximum = db.Database.ReadSamples(Principal, db.Partition, SetName, Series, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue);
        var withOffset = db.Database.ReadSamples(Principal, db.Partition, SetName, Series, offset, offset);

        await Assert.That(minimum.Select(record => record.Sample.EventId)).IsEquivalentTo(new[] { MinimumId }, CollectionOrdering.Matching);
        await Assert.That(maximum.Select(record => record.Sample.EventId)).IsEquivalentTo(new[] { MaximumFirstId, MaximumSecondId }, CollectionOrdering.Matching);
        await Assert.That(withOffset.Select(record => record.Sample.EventId)).IsEquivalentTo(new[] { OffsetId }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcMp005CompleteArrayBudgetAcceptsItsExactBoundaryAndRejectsOneByteLess()
    {
        using var db = new TestDatabase();
        db.Configure(SetName, ResourceKind.TimeSeries);
        db.Commit(new AppendSamples(SetName, Series, [new(SelectedId, Timestamp, 1)]));
        var expected = db.Database.ReadSamples(Principal, db.Partition, SetName, Series, Timestamp, Timestamp);
        var length = JsonDefaults.Serialize(expected).Length;
        var exact = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxBatchBytes = length }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var shortBudget = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxBatchBytes = length - 1 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());

        var actual = exact.ReadSamples(Principal, db.Partition, SetName, Series, Timestamp, Timestamp);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => shortBudget.ReadSamples(Principal, db.Partition, SetName, Series, Timestamp, Timestamp));

        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcMp005ProjectionUsesPersistedPolicyAndCancellationKeepsStoreUsable()
    {
        using var db = new TestDatabase();
        db.Configure(SetName, ResourceKind.TimeSeries, fields: [new(ProtectedPath, PrivateClassification)]);
        db.Commit(new AppendSamples(SetName, Series, [new(SelectedId, Timestamp, 1)], ProtectedTags));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader, db.Partition.TenantId,
            [new(db.Partition.DatabaseId, SetName, Capability.SeriesRead)], []))).Get<PrincipalRecord>();
        var visible = db.Database.ReadSamples(Reader, db.Partition, SetName, Series, Timestamp, Timestamp);
        await Assert.That(visible[0].TagsJson).DoesNotContain(ProtectedValue);
        var position = db.Store.Position;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.That(() => db.Database.ReadSamples(Principal, db.Partition, SetName, Series, Timestamp, Timestamp,
            cancellationToken: cancelled.Token)).Throws<OperationCanceledException>();
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(db.Database.ReadSamples(Principal, db.Partition, SetName, Series, Timestamp, Timestamp)).HasSingleItem();
    }
}
