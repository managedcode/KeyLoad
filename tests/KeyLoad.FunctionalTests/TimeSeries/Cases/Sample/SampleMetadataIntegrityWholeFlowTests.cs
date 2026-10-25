namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class SampleMetadataIntegrityWholeFlowTests
{
    [Test]
    [Arguments(SampleMetadataIntegrityFault.RetentionFloor)]
    [Arguments(SampleMetadataIntegrityFault.RetentionCount)]
    [Arguments(SampleMetadataIntegrityFault.RetentionVersion)]
    [Arguments(SampleMetadataIntegrityFault.RetentionRoot)]
    [Arguments(SampleMetadataIntegrityFault.RetentionWire)]
    [Arguments(SampleMetadataIntegrityFault.BucketEndpoint)]
    [Arguments(SampleMetadataIntegrityFault.BucketCount)]
    [Arguments(SampleMetadataIntegrityFault.BucketSequence)]
    [Arguments(SampleMetadataIntegrityFault.BucketFloor)]
    [Arguments(SampleMetadataIntegrityFault.BucketVersion)]
    public async Task AcSeries016022023MetadataRefusesAcrossColdThenExactRepairReplayAndCorrectionStayHealthy(
        SampleMetadataIntegrityFault fault)
    {
        using var fixture = new SampleMetadataIntegrityFixture();
        await SampleMetadataIntegrityAssertions.Healthy(fixture, 1, appended: false);
        var originalCut = SampleMetadataIntegrityAssertions.Capture(fixture.Store);
        var original = SampleMetadataIntegrityScenario.Original(fixture, fault);
        var refresh = fixture.Operation(SampleMetadataIntegrityScenario.Refresh(1));
        var expiry = fixture.Operation(new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            SampleMetadataIntegrityScenario.Floor.AddSeconds(1), 1));
        SampleMetadataIntegrityScenario.Inject(fixture, fault, original);
        await SampleMetadataIntegrityAssertions.Refuse(fixture, fault, refresh, expiry);
        var corrupt = SampleMetadataIntegrityAssertions.Capture(fixture.Store);
        await Assert.That(corrupt.Position).IsEqualTo(originalCut.Position + 1);
        fixture.Reopen();
        await SampleMetadataIntegrityAssertions.Unchanged(fixture, corrupt);
        await SampleMetadataIntegrityAssertions.Refuse(fixture, fault, refresh, expiry);
        SampleMetadataIntegrityScenario.Put(fixture, fault, original);
        await SampleMetadataIntegrityAssertions.Unchanged(fixture, originalCut with
        {
            Position = originalCut.Position + 2
        });
        var repaired = fixture.Apply(refresh);
        await SampleMetadataIntegrityAssertions.Healthy(fixture, 2, appended: false);
        await SampleMetadataIntegrityAssertions.Replay(fixture, refresh, repaired);
        await SampleMetadataIntegrityAssertions.Replay(fixture, fixture.BucketOperation, fixture.BucketReceipt);
        var append = fixture.Operation(new AppendSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            [SampleMetadataIntegrityScenario.Sample(5)], SampleAggregateTestData.EmptyTags));
        var appended = fixture.Apply(append);
        await SampleMetadataIntegrityAssertions.Failure(fixture, () => SampleMetadataIntegrityScenario.Bucket(fixture),
            ErrorCode.HistoryUnavailable);
        var correction = fixture.Operation(SampleMetadataIntegrityScenario.Refresh(2));
        var corrected = fixture.Apply(correction);
        await SampleMetadataIntegrityAssertions.Healthy(fixture, 3, appended: true);
        var healthy = SampleMetadataIntegrityAssertions.Capture(fixture.Store);
        fixture.Reopen();
        await SampleMetadataIntegrityAssertions.Unchanged(fixture, healthy);
        await SampleMetadataIntegrityAssertions.Healthy(fixture, 3, appended: true);
        await SampleMetadataIntegrityAssertions.Replay(fixture, fixture.SeedOperation, fixture.SeedReceipt);
        await SampleMetadataIntegrityAssertions.Replay(fixture, fixture.BucketOperation, fixture.BucketReceipt);
        await SampleMetadataIntegrityAssertions.Replay(fixture, refresh, repaired);
        await SampleMetadataIntegrityAssertions.Replay(fixture, append, appended);
        await SampleMetadataIntegrityAssertions.Replay(fixture, correction, corrected);
        await SampleMetadataIntegrityAssertions.Healthy(fixture, 3, appended: true);
    }
}
