namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRawIntegrityWholeFlowTests
{
    [Test]
    [Arguments(SampleRawIntegrityFault.Series)]
    [Arguments(SampleRawIntegrityFault.Timestamp)]
    [Arguments(SampleRawIntegrityFault.Sequence)]
    [Arguments(SampleRawIntegrityFault.NonpositiveSequence)]
    [Arguments(SampleRawIntegrityFault.NonfiniteValue)]
    [Arguments(SampleRawIntegrityFault.MissingSample)]
    [Arguments(SampleRawIntegrityFault.WrongNativeRoot)]
    [Arguments(SampleRawIntegrityFault.MalformedNative)]
    public async Task AcSeries005008009010011016RawIntegrityRefusesAcrossColdThenExactRepairAndReplayRemainHealthy(
        SampleRawIntegrityFault fault)
    {
        using var fixture = new SampleRawIntegrityFixture();
        await SampleRawIntegrityAssertions.HealthyAsync(fixture, final: false);
        var original = SampleRawIntegrityScenario.OriginalBytes(fixture);
        var expiry = SampleRawIntegrityScenario.Expiry(fixture);
        SampleRawIntegrityScenario.Inject(fixture, fault);
        await SampleRawIntegrityAssertions.RefuseAsync(fixture, expiry);
        var corruptCut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        fixture.Reopen();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, corruptCut);
        await SampleRawIntegrityAssertions.RefuseAsync(fixture, expiry);
        SampleRawIntegrityScenario.Restore(fixture, original);
        var expired = fixture.Apply(expiry);
        await SampleRawIntegrityAssertions.ExpiryReceiptAsync(fixture, expired, expiry);
        await SampleRawIntegrityAssertions.ReplayAsync(fixture, expiry, expired);
        await SampleRawIntegrityAssertions.ReplayAsync(fixture, fixture.SeedOperation, fixture.SeedReceipt);

        var append = fixture.Operation(new AppendSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            [SampleRawIntegrityScenario.Sample(5)], SampleAggregateTestData.EmptyTags));
        var healthyReceipt = fixture.Apply(append);
        await SampleRawIntegrityAssertions.HealthyAsync(fixture, final: true);
        var duplicate = fixture.Operation(new AppendSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            [SampleRawIntegrityScenario.Sample(1)], SampleAggregateTestData.EmptyTags));
        var duplicateReceipt = fixture.Apply(duplicate);
        await SampleRawIntegrityAssertions.HealthyAsync(fixture, final: true);
        var healthyCut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        fixture.Reopen();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, healthyCut);
        await SampleRawIntegrityAssertions.HealthyAsync(fixture, final: true);
        await SampleRawIntegrityAssertions.ReplayAsync(fixture, expiry, expired);
        await SampleRawIntegrityAssertions.ReplayAsync(fixture, fixture.SeedOperation, fixture.SeedReceipt);
        await SampleRawIntegrityAssertions.ReplayAsync(fixture, append, healthyReceipt);
        await SampleRawIntegrityAssertions.ReplayAsync(fixture, duplicate, duplicateReceipt);
        await SampleRawIntegrityAssertions.HealthyAsync(fixture, final: true);
    }
}
