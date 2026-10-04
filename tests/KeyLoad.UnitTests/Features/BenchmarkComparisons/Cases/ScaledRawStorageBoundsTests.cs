using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

[NotInParallel]
internal sealed class ScaledRawStorageBoundsTests
{
    private const int MiniRecordCount = 4;
    private const int QualificationRecordCount = 100_000;
    private const int SmallPayloadBytes = 32;
    private const int FirstRecordIndex = 0;
    private const int InvalidRecordIndex = -1;
    private const int BeyondReservedMissIndex = MiniRecordCount + 1;
    private const long OneNativeRead = 1L;
    private const int InitialVerificationPasses = 1;
    private const int RepeatedVerificationPasses = 2;
    private const long GibibyteBytes = 1024L * 1024 * 1024;
    private const long MiniHeadroomBytes = 2L * GibibyteBytes;
    private const long QualificationMemoryCeilingBytes = 12L * GibibyteBytes;
    private const long QualificationHeadroomBytes = 14L * GibibyteBytes;

    [Test]
    public async Task AcScale003InvalidPointReadsDoNotChargeAndTheMiniFixtureRemainsGenuine()
    {
        await ScaledRawStorageTestLifetime.RunAsync(
            () => new ScaledRawStorageFixture(MiniRecordCount, SmallPayloadBytes),
            AssertMiniReadBoundsAndOracleAsync);
    }

    [Test]
    public async Task AcScale003MiniFixtureReportsActualPeakAndHeadroom()
    {
        await ScaledRawStorageTestLifetime.RunAsync(
            () => new ScaledRawStorageFixture(MiniRecordCount, SmallPayloadBytes),
            AssertMiniMemoryBoundsAsync);
    }

    [Test]
    public async Task AcScale003HundredThousandFixtureMeetsActualQualificationCapacity()
    {
        await ScaledRawStorageTestLifetime.RunAsync(
            () => new ScaledRawStorageFixture(
                QualificationRecordCount,
                SmallPayloadBytes,
                TestContext.Current!.Execution.CancellationToken),
            AssertQualificationFixtureAsync);
    }

    private static async Task AssertMiniReadBoundsAndOracleAsync(ScaledRawStorageFixture fixture)
    {
        var initial = fixture.Capture();
        await AssertInitialMiniOracleAsync(initial);
        await AssertInvalidPointReadAsync(fixture, () => fixture.Read(InvalidRecordIndex));
        await AssertInvalidPointReadAsync(fixture, () => fixture.Read(BeyondReservedMissIndex));
        await AssertInvalidPointReadAsync(fixture, () => fixture.TryRead(InvalidRecordIndex, out _));
        await AssertInvalidPointReadAsync(fixture, () => fixture.TryRead(BeyondReservedMissIndex, out _));

        var beforeMiss = fixture.Capture();
        await Assert.That(fixture.TryRead(MiniRecordCount, out _)).IsFalse();
        await AssertCounterDeltaAsync(beforeMiss, fixture.Capture(), OneNativeRead);

        var beforeRead = fixture.Capture();
        await Assert.That(fixture.Read(FirstRecordIndex)).IsEqualTo((ulong)FirstRecordIndex);
        await AssertCounterDeltaAsync(beforeRead, fixture.Capture(), OneNativeRead);
        await AssertCompleteMiniOracleAsync(fixture, initial.FullValueDigest);
    }

    private static async Task AssertInvalidPointReadAsync(
        ScaledRawStorageFixture fixture,
        Action invalidRead)
    {
        var before = fixture.Capture();
        await Assert.That(invalidRead).Throws<ArgumentOutOfRangeException>();
        var after = fixture.Capture();
        await Assert.That(after.NativeReadCalls).IsEqualTo(before.NativeReadCalls);
        await Assert.That(after.SeedAttempts).IsEqualTo(before.SeedAttempts);
        await Assert.That(after.SuccessfulSeedWrites).IsEqualTo(before.SuccessfulSeedWrites);
    }

    private static async Task AssertInitialMiniOracleAsync(ScaledRawStorageSnapshot snapshot)
    {
        await Assert.That(snapshot.RecordCount).IsEqualTo(MiniRecordCount);
        await Assert.That(snapshot.PayloadBytes).IsEqualTo(SmallPayloadBytes);
        await Assert.That(snapshot.SeedAttempts).IsEqualTo(MiniRecordCount);
        await Assert.That(snapshot.SuccessfulSeedWrites).IsEqualTo(MiniRecordCount);
        await Assert.That(snapshot.VerifiedRecords).IsEqualTo(MiniRecordCount);
        await Assert.That(snapshot.VerificationPasses).IsEqualTo(InitialVerificationPasses);
        await Assert.That(snapshot.NativeReadCalls).IsEqualTo(MiniRecordCount + 1L);
    }

    private static async Task AssertCompleteMiniOracleAsync(
        ScaledRawStorageFixture fixture,
        string initialDigest)
    {
        var beforeVerify = fixture.Capture();
        fixture.VerifyAll();
        var afterVerify = fixture.Capture();
        await Assert.That(afterVerify.VerifiedRecords).IsEqualTo(MiniRecordCount);
        await Assert.That(afterVerify.VerificationPasses).IsEqualTo(RepeatedVerificationPasses);
        await Assert.That(afterVerify.NativeReadCalls - beforeVerify.NativeReadCalls)
            .IsEqualTo(MiniRecordCount + 1L);
        await Assert.That(afterVerify.FullValueDigest).IsEqualTo(initialDigest);
    }

    private static async Task AssertMiniMemoryBoundsAsync(ScaledRawStorageFixture fixture)
    {
        var snapshot = fixture.Capture();
        await Assert.That(snapshot.ProcessMemoryCeilingBytes).IsGreaterThan(0L);
        await Assert.That(snapshot.ProcessMemoryCeilingBytes).IsLessThan(QualificationMemoryCeilingBytes);
        await Assert.That(snapshot.ProcessPeakBytes).IsGreaterThan(0L);
        await Assert.That(snapshot.ProcessPeakBytes).IsLessThanOrEqualTo(snapshot.ProcessMemoryCeilingBytes);
        await Assert.That(snapshot.EffectiveMemoryCapacityBytes)
            .IsGreaterThanOrEqualTo(snapshot.ProcessPeakBytes + MiniHeadroomBytes);
    }

    private static async Task AssertQualificationFixtureAsync(ScaledRawStorageFixture fixture)
    {
        var snapshot = fixture.Capture();
        await Assert.That(snapshot.RecordCount).IsEqualTo(QualificationRecordCount);
        await Assert.That(snapshot.PayloadBytes).IsEqualTo(SmallPayloadBytes);
        await Assert.That(snapshot.EffectiveMemoryCapacityBytes)
            .IsGreaterThanOrEqualTo(QualificationHeadroomBytes);
        await Assert.That(snapshot.ProcessMemoryCeilingBytes).IsEqualTo(QualificationMemoryCeilingBytes);
        await Assert.That(snapshot.ProcessPeakBytes).IsGreaterThan(0L);
        await Assert.That(snapshot.ProcessPeakBytes).IsLessThanOrEqualTo(QualificationMemoryCeilingBytes);
        await Assert.That(snapshot.SeedAttempts).IsEqualTo(QualificationRecordCount);
        await Assert.That(snapshot.SuccessfulSeedWrites).IsEqualTo(QualificationRecordCount);
        await Assert.That(snapshot.VerifiedRecords).IsEqualTo(QualificationRecordCount);
        await Assert.That(snapshot.VerificationPasses).IsEqualTo(InitialVerificationPasses);
        await Assert.That(snapshot.NativeReadCalls).IsEqualTo(QualificationRecordCount + 1L);
    }

    private static async Task AssertCounterDeltaAsync(
        ScaledRawStorageSnapshot before,
        ScaledRawStorageSnapshot after,
        long expectedReadDelta)
    {
        await Assert.That(after.NativeReadCalls - before.NativeReadCalls).IsEqualTo(expectedReadDelta);
        await Assert.That(after.SeedAttempts).IsEqualTo(before.SeedAttempts);
        await Assert.That(after.SuccessfulSeedWrites).IsEqualTo(before.SuccessfulSeedWrites);
    }
}
