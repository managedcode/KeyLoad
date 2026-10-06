using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Verifies real fixture cancellation, invalid-input and repeat-close boundaries.</summary>
[NotInParallel]
internal sealed class ScaledRawStorageFixtureLifetimeTests
{
    private const int MiniRecordCount = 4;
    private const int SmallPayloadBytes = 32;
    private const int InvalidRecordCount = 0;
    private const int InvalidPayloadBytes = 31;
    private const int FirstRecordIndex = 0;

    [Test]
    public async Task AcScale003RejectsPreCancelledAndInvalidFixtureInputs()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.That(() => CreateCancelledFixture(cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(() => CreateFixture(InvalidRecordCount, SmallPayloadBytes))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateFixture(MiniRecordCount, InvalidPayloadBytes))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task AcScale003ClosedFixtureRejectsReadsAndCanBeClosedRepeatedly()
    {
        await ScaledRawStorageTestLifetime.RunAsync(
            () => new ScaledRawStorageFixture(MiniRecordCount, SmallPayloadBytes, UnitBenchmarkOptions.ScaledStorage()), async fixture =>
            {
                fixture.VerifyAll();
                fixture.Dispose();
                fixture.Dispose();
                await Assert.That(() => fixture.TryRead(FirstRecordIndex, out _)).Throws<ObjectDisposedException>();
                await Assert.That(() => fixture.Read(FirstRecordIndex)).Throws<ObjectDisposedException>();
            });
    }

    private static void CreateCancelledFixture(CancellationToken cancellationToken)
    {
        using var fixture = new ScaledRawStorageFixture(MiniRecordCount, SmallPayloadBytes, UnitBenchmarkOptions.ScaledStorage(), cancellationToken);
    }

    private static void CreateFixture(int recordCount, int payloadBytes)
    {
        using var fixture = new ScaledRawStorageFixture(recordCount, payloadBytes, UnitBenchmarkOptions.ScaledStorage());
    }
}
