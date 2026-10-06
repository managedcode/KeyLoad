using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

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
        var limits = database.Database.Limits with { QueryDeadlineSeconds = 1 };
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits), database.Database.EvaluationClock);
        budget.ChargeBytes(cut.Encoded.Length);
        await Task.Delay(TimeSpan.FromMilliseconds(1_100));

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Decode(cut.Encoded, budget, UnitExecutionOptions.TimeSeriesExecution()));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await SampleChunkStoreFailureAssertions.AssertUnchangedAndReadableAsync(fixture, before);
    }
}

internal static class SampleChunkStoreFailureAssertions
{
    internal static async Task AssertUnchangedAndReadableAsync(SampleChunkStoreFixture fixture,
        SampleChunkStoreCanonicalState before)
    {
        var after = SampleChunkStoreFixture.CaptureCanonicalState(fixture.Database);
        var visible = fixture.Database.Database.ReadSamples(SampleChunkStoreFixture.Root,
            fixture.Database.Partition, SampleChunkStoreFixture.Set, SampleChunkStoreFixture.Series,
            SampleChunkStoreFixture.Floor, SampleChunkStoreFixture.EndExclusive);

        await SampleChunkStoreAssertions.CanonicalStateAsync(before, after);
        await Assert.That(visible.Length).IsEqualTo(6);
        await Assert.That(visible.Any(sample => sample.Sample.EventId == "chunk-old")).IsFalse();
        await Assert.That(fixture.Database.Database.ReadSampleRetention(SampleChunkStoreFixture.Root,
            new(fixture.Database.Partition, SampleChunkStoreFixture.Set, SampleChunkStoreFixture.Series)).Before)
            .IsEqualTo(SampleChunkStoreFixture.Floor);
    }
}
