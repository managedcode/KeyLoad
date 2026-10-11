using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleRangeBudgetAssertions
{
    private const string InvalidRange = "The series read budget is invalid.";
    private const string CumulativeExceeded = "The series result byte budget is exceeded.";
    private const string AppendKind = "appendSamples";
    private const int OneByte = 1;
    private const int OriginalMaximumSequence = 4;
    private const int AppendedMaximumSequence = 5;

    internal static async Task RefuseAsync(SampleRawIntegrityFixture fixture, SampleRangeBudgetFault fault)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        if (fault == SampleRangeBudgetFault.CumulativeResponse)
        { await CumulativeAsync(fixture); }
        else
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => RefuseInvalid(fixture, fault));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(failure.Message).IsEqualTo(InvalidRange);
        }
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
    }

    private static void RefuseInvalid(SampleRawIntegrityFixture fixture, SampleRangeBudgetFault fault)
    {
        var owner = fixture.Owner;
        switch (fault)
        {
            case SampleRangeBudgetFault.InvertedRange:
                SampleRangeBudgetWholeFlow.ReadPair(fixture, owner,
                    from: SampleRawIntegrityScenario.Sample(3).Timestamp,
                    until: SampleRawIntegrityScenario.InitialFloor);
                break;
            case SampleRangeBudgetFault.ZeroLimit:
                SampleRangeBudgetWholeFlow.ReadPair(fixture, owner, limit: 0);
                break;
            case SampleRangeBudgetFault.ExcessLimit:
                SampleRangeBudgetWholeFlow.ReadPair(fixture, owner, limit: checked(owner.Limits.MaxResults + 1));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }

    private static async Task CumulativeAsync(SampleRawIntegrityFixture fixture)
    {
        var expected = new[] { SampleRawIntegrityScenario.Record(2), SampleRawIntegrityScenario.Record(3) };
        var maximum = checked(JsonDefaults.Serialize(expected[0]).Length + JsonDefaults.Serialize(expected[1]).Length - OneByte);
        var bounded = SampleRangeBudgetWholeFlow.BoundedOwner(fixture, maximum);
        foreach (var row in expected)
        {
            await Assert.That(JsonDefaults.Serialize(new[] { row }).Length).IsLessThanOrEqualTo(maximum);
            var one = SampleRangeBudgetWholeFlow.ReadPair(fixture, bounded, row.Sample.Timestamp, row.Sample.Timestamp);
            await SameRowsAsync(one, [row]);
        }
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => SampleRangeBudgetWholeFlow.ReadPair(fixture, bounded));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo(CumulativeExceeded);
        var exact = SampleRangeBudgetWholeFlow.BoundedOwner(fixture, JsonDefaults.Serialize(expected).Length);
        await SameRowsAsync(SampleRangeBudgetWholeFlow.ReadPair(fixture, exact), expected);
    }

    internal static async Task HealthyRowsAsync(SampleRawIntegrityFixture fixture, bool appended)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var maximum = appended ? AppendedMaximumSequence : OriginalMaximumSequence;
        var expected = Enumerable.Range(2, maximum - 1).Select(SampleRawIntegrityScenario.Record).ToArray();
        var actual = SampleRangeBudgetWholeFlow.ReadPair(fixture, fixture.Owner,
            until: SampleRawIntegrityScenario.Sample(maximum).Timestamp);
        await SameRowsAsync(actual, expected);
        var retention = fixture.Owner.ReadSampleRetention(SampleAggregateTestData.RootPrincipal,
            new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));
        await Assert.That(retention).IsEqualTo(new SampleRetentionStatus(SampleRawIntegrityScenario.InitialFloor, 1, false));
        var latest = fixture.Owner.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                SampleRawIntegrityScenario.Sample(maximum).Timestamp));
        await Assert.That(latest.Sample).IsEqualTo(expected[^1]);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
    }

    internal static async Task AppendReceiptAsync(SampleRawIntegrityFixture fixture, ReplicatedOperation operation,
        CommitReceipt receipt, long previousPosition)
    {
        var nextPosition = checked(previousPosition + 1);
        await Assert.That(fixture.Store.Position).IsEqualTo(nextPosition);
        var expected = new CommitReceipt(operation.Id,
            new(fixture.Store.Identity.Incarnation, fixture.Partition.AtomicPartitionId, nextPosition, 1),
            [new(AppendKind, SampleAggregateTestData.Set, SampleAggregateTestData.Series, AppendedMaximumSequence)],
            DurabilityProfile.ProcessDurable);
        await Assert.That(NativeSerialization.Serialize(receipt).SequenceEqual(
            NativeSerialization.Serialize(expected))).IsTrue();
    }

    private static async Task SameRowsAsync(SampleRecord[] actual, SampleRecord[] expected)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(NativeSerialization.Serialize(actual[index]).SequenceEqual(
                NativeSerialization.Serialize(expected[index]))).IsTrue();
        }
    }
}
