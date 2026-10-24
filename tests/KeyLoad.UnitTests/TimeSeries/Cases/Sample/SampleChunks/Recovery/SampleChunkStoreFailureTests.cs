using KeyLoad.Core.TimeSeries;

namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class SampleChunkStoreFailureTests
{
    [Test]
    public async Task AcChunk003SmallerEncodedByteCeilingRejectsWholeCandidateWithoutChangingCanonicalState()
    {
        using var fixture = SampleChunkStoreFixture.Create();
        var database = fixture.Database;
        var cut = fixture.CaptureCodecCut();
        var before = SampleChunkStoreFixture.CaptureCanonicalState(database);
        var ceiling = cut.Encoded.Length - 1;

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Encode(
            cut.Source.AsSpan(), SampleChunkStoreFixture.NewBudget(database), UnitExecutionOptions.TimeSeriesExecution(), ceiling));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await SampleChunkStoreFailureAssertions.AssertUnchangedAndReadableAsync(fixture, before);
    }

    [Test]
    public async Task AcChunk003CancellationRejectsWholeCandidateWithoutChangingCanonicalState()
    {
        using var fixture = SampleChunkStoreFixture.Create();
        var database = fixture.Database;
        var cut = fixture.CaptureCodecCut();
        var before = SampleChunkStoreFixture.CaptureCanonicalState(database);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => SampleChunkCodec.Encode(
            cut.Source.AsSpan(), SampleChunkStoreFixture.NewBudget(database, cancellation.Token), UnitExecutionOptions.TimeSeriesExecution()));
        await Assert.That(failure).IsNotNull();
        await SampleChunkStoreFailureAssertions.AssertUnchangedAndReadableAsync(fixture, before);
    }

    [Test]
    public async Task AcChunk003ExpiredWorkDeadlineRejectsWholeCandidateWithoutChangingCanonicalState()
    {
        using var fixture = SampleChunkStoreFixture.Create();
        var database = fixture.Database;
        var cut = fixture.CaptureCodecCut();
        var before = SampleChunkStoreFixture.CaptureCanonicalState(database);
        var limits = database.Database.Limits with
        {
            QueryDeadlineSeconds = 1
        };
        var clock = new SampleRollupWorkClock();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits), clock);
        budget.ChargeBytes(cut.Encoded.Length);
        clock.Advance(TimeSpan.FromMilliseconds(1_100));

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Decode(cut.Encoded, budget, UnitExecutionOptions.TimeSeriesExecution()));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await SampleChunkStoreFailureAssertions.AssertUnchangedAndReadableAsync(fixture, before);
    }
}
