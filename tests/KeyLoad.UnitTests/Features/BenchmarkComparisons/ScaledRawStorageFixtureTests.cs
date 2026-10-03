using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Exercises genuine resident ZoneTree fixtures and independent all-record value verification.</summary>
[NotInParallel]
internal sealed class ScaledRawStorageFixtureTests
{
    private const int MiniRecordCount = 4;
    private const int ScaledRecordCount = 100_000;
    private const int SmallPayloadBytes = 32;
    private const int LargePayloadBytes = 1024;
    private const int FirstRecordIndex = 0;
    private const int ReservedMissCount = 1;
    private const int KeyBytes = 16;
    private const int OrderEntryBytes = 4;
    private const int OneVerificationPass = 1;
    private const int TwoVerificationPasses = 2;
    private const int ThreeVerificationPasses = 3;

    [Test]
    [Arguments(SmallPayloadBytes)]
    [Arguments(LargePayloadBytes)]
    public async Task AcScale002And003MiniFixturesSeedVerifyAndRetainExactZoneTreeValues(int payloadBytes)
    {
        await ScaledRawStorageTestLifetime.RunAsync(
            () => new ScaledRawStorageFixture(MiniRecordCount, payloadBytes), async fixture =>
            {
                var before = fixture.Capture();

                await Assert.That(before.SeedAttempts).IsEqualTo(MiniRecordCount);
                await Assert.That(before.SuccessfulSeedWrites).IsEqualTo(MiniRecordCount);
                await Assert.That(before.RecordCount).IsEqualTo(MiniRecordCount);
                await Assert.That(before.PayloadBytes).IsEqualTo(payloadBytes);
                await Assert.That(before.VerifiedRecords).IsEqualTo(MiniRecordCount);
                await Assert.That(before.VerificationPasses).IsEqualTo(OneVerificationPass);
                await Assert.That(before.NativeReadCalls).IsEqualTo(MiniRecordCount + ReservedMissCount);
                await Assert.That(before.RetainedKeyBytes).IsEqualTo((long)(MiniRecordCount + 1) * KeyBytes);
                await Assert.That(before.RetainedValueBytes).IsEqualTo(
                    ExpectedRetainedValueBytes(MiniRecordCount, payloadBytes));
                await Assert.That(before.RetainedOrderBytes).IsEqualTo((long)MiniRecordCount * OrderEntryBytes);
                await Assert.That(before.PermutationDigest)
                    .IsEqualTo(new ScaledRawStorageReadOrder(MiniRecordCount).PermutationDigest);
                await Assert.That(ScaledRawStorageFixtureOracleTests.IsSha256(before.FullValueDigest)).IsTrue();
                await Assert.That(before.FullValueDigest)
                    .IsEqualTo(ScaledRawStorageFixtureOracleTests.IndependentValueDigest(MiniRecordCount, payloadBytes));

                fixture.VerifyAll();
                var after = fixture.Capture();
                await Assert.That(after.VerifiedRecords).IsEqualTo(MiniRecordCount);
                await Assert.That(after.VerificationPasses).IsEqualTo(TwoVerificationPasses);
                await Assert.That(after.NativeReadCalls - before.NativeReadCalls)
                    .IsEqualTo(MiniRecordCount + ReservedMissCount);
                await Assert.That(after.FullValueDigest).IsEqualTo(before.FullValueDigest);
                await Assert.That(fixture.Read(FirstRecordIndex)).IsEqualTo((ulong)FirstRecordIndex);
                await Assert.That(fixture.TryRead(MiniRecordCount, out _)).IsFalse();
                await ScaledRawStorageFixtureOracleTests.AssertFixtureOrdersAsync(fixture, MiniRecordCount);
                var beforePostReadVerification = fixture.Capture();
                fixture.VerifyAll();
                var afterPostReadVerification = fixture.Capture();
                await Assert.That(afterPostReadVerification.VerificationPasses).IsEqualTo(ThreeVerificationPasses);
                await Assert.That(afterPostReadVerification.NativeReadCalls - beforePostReadVerification.NativeReadCalls)
                    .IsEqualTo(MiniRecordCount + ReservedMissCount);
                await Assert.That(afterPostReadVerification.FullValueDigest).IsEqualTo(before.FullValueDigest);
                await ScaledRawStorageFixtureOracleTests.AssertNativeResidenceAsync(
                    afterPostReadVerification, MiniRecordCount);
            });
    }

    [Test]
    [Arguments(SmallPayloadBytes)]
    [Arguments(LargePayloadBytes)]
    public async Task AcScale002And003GenuineHundredThousandZoneTreeRowsPassFullValueOracle(int payloadBytes)
    {
        await ScaledRawStorageTestLifetime.RunAsync(
            () => new ScaledRawStorageFixture(ScaledRecordCount, payloadBytes,
            TestContext.Current!.Execution.CancellationToken), async fixture =>
            {
                var seeded = fixture.Capture();

                await Assert.That(seeded.SeedAttempts).IsEqualTo(ScaledRecordCount);
                await Assert.That(seeded.SuccessfulSeedWrites).IsEqualTo(ScaledRecordCount);
                await Assert.That(seeded.VerifiedRecords).IsEqualTo(ScaledRecordCount);
                await Assert.That(seeded.VerificationPasses).IsEqualTo(OneVerificationPass);
                await Assert.That(seeded.NativeReadCalls).IsEqualTo(ScaledRecordCount + ReservedMissCount);
                await Assert.That(seeded.RetainedKeyBytes).IsEqualTo((long)(ScaledRecordCount + 1) * KeyBytes);
                await Assert.That(seeded.RetainedValueBytes).IsEqualTo(
                    ExpectedRetainedValueBytes(ScaledRecordCount, payloadBytes));
                await Assert.That(seeded.RetainedOrderBytes).IsEqualTo((long)ScaledRecordCount * OrderEntryBytes);
                await Assert.That(seeded.PermutationDigest)
                    .IsEqualTo(new ScaledRawStorageReadOrder(ScaledRecordCount).PermutationDigest);
                await Assert.That(seeded.FullValueDigest)
                    .IsEqualTo(ScaledRawStorageFixtureOracleTests.IndependentValueDigest(ScaledRecordCount, payloadBytes));

                fixture.VerifyAll();
                var verified = fixture.Capture();
                await Assert.That(verified.VerifiedRecords).IsEqualTo(ScaledRecordCount);
                await Assert.That(verified.VerificationPasses).IsEqualTo(TwoVerificationPasses);
                await Assert.That(verified.NativeReadCalls - seeded.NativeReadCalls)
                    .IsEqualTo(ScaledRecordCount + ReservedMissCount);
                await Assert.That(verified.FullValueDigest).IsEqualTo(seeded.FullValueDigest);
                await ScaledRawStorageFixtureOracleTests.AssertFixtureOrdersAsync(fixture, ScaledRecordCount);
                var beforePostReadVerification = fixture.Capture();
                fixture.VerifyAll();
                var afterPostReadVerification = fixture.Capture();
                await Assert.That(afterPostReadVerification.VerificationPasses).IsEqualTo(ThreeVerificationPasses);
                await Assert.That(afterPostReadVerification.NativeReadCalls - beforePostReadVerification.NativeReadCalls)
                    .IsEqualTo(ScaledRecordCount + ReservedMissCount);
                await Assert.That(afterPostReadVerification.FullValueDigest).IsEqualTo(seeded.FullValueDigest);
                await ScaledRawStorageFixtureOracleTests.AssertNativeResidenceAsync(
                    afterPostReadVerification, ScaledRecordCount);
            });
    }

    private static long ExpectedRetainedValueBytes(int count, int payloadBytes)
        => (long)count * payloadBytes;

}
