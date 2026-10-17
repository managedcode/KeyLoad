using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class DatabasePhaseArithmeticTests
{
    private const long MegahertzFrequency = 1_000_000;
    private const int BucketCount = 16;
    private const int ContentionAttempts = 20_000;

    private static readonly long[] MegahertzBoundaries =
    [50, 100, 250, 500, 1_000, 2_000, 5_000, 10_000, 20_000, 50_000,
        100_000, 250_000, 500_000, 1_000_000, 5_000_000];

    [Test]
    public async Task InclusiveFrequencyScaledBoundariesAndAdjacentTicksSelectExpectedBuckets()
    {
        for (var bucket = 0; bucket < MegahertzBoundaries.Length; bucket++)
        {
            var boundary = MegahertzBoundaries[bucket];
            await Assert.That(DatabasePhaseArithmetic.BucketFor(boundary - 1, MegahertzFrequency)).IsEqualTo(bucket);
            await Assert.That(DatabasePhaseArithmetic.BucketFor(boundary, MegahertzFrequency)).IsEqualTo(bucket);
            await Assert.That(DatabasePhaseArithmetic.BucketFor(boundary + 1, MegahertzFrequency)).IsEqualTo(bucket + 1);
        }

        await Assert.That(DatabasePhaseArithmetic.BucketFor(0, MegahertzFrequency)).IsEqualTo(0);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(5_000_001, MegahertzFrequency)).IsEqualTo(BucketCount - 1);
    }

    [Test]
    public async Task LowFrequencyCollapsedBoundariesAndOverflowSizedInputsRemainDefined()
    {
        await Assert.That(DatabasePhaseArithmetic.BucketFor(0, 1)).IsEqualTo(0);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(1, 1)).IsEqualTo(13);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(5, 1)).IsEqualTo(14);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(6, 1)).IsEqualTo(15);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(0, long.MaxValue)).IsEqualTo(0);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(long.MaxValue / 2, long.MaxValue)).IsEqualTo(12);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(long.MaxValue, long.MaxValue)).IsEqualTo(13);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(long.MaxValue, 1)).IsEqualTo(15);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(1, 3)).IsEqualTo(12);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(2, 3)).IsEqualTo(13);
        var boundaries = DatabasePhaseArithmetic.CreateBoundaries(long.MaxValue);
        await Assert.That(boundaries.All(static boundary => boundary >= 0)).IsTrue();
        await Assert.That(boundaries.SequenceEqual(boundaries.Order())).IsTrue();
        await Assert.That(boundaries[13]).IsEqualTo(long.MaxValue);
        await Assert.That(boundaries[14]).IsEqualTo(long.MaxValue);
    }

    [Test]
    public async Task InvalidElapsedAndFrequencyReturnTheUnavailableBucket()
    {
        await Assert.That(DatabasePhaseArithmetic.BucketFor(-1, MegahertzFrequency)).IsEqualTo(-1);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(0, 0)).IsEqualTo(-1);
        await Assert.That(DatabasePhaseArithmetic.BucketFor(0, -1)).IsEqualTo(-1);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task IncrementSaturatesWithoutWrappingAndReportsDegradation(int maximumCasAttempts)
    {
        long counter = 0;
        await Assert.That(DatabasePhaseArithmetic.TryIncrement(ref counter, maximumCasAttempts)).IsEqualTo(DatabaseProfileQuality.None);
        await Assert.That(counter).IsEqualTo(1);

        counter = long.MaxValue - 1;
        await Assert.That(DatabasePhaseArithmetic.TryIncrement(ref counter, maximumCasAttempts)).IsEqualTo(DatabaseProfileQuality.SaturatedCounter);
        await Assert.That(counter).IsEqualTo(long.MaxValue);
        await Assert.That(DatabasePhaseArithmetic.TryIncrement(ref counter, maximumCasAttempts)).IsEqualTo(DatabaseProfileQuality.SaturatedCounter);
        await Assert.That(counter).IsEqualTo(long.MaxValue);

        counter = -1;
        await Assert.That(DatabasePhaseArithmetic.TryIncrement(ref counter, maximumCasAttempts)).IsEqualTo(DatabaseProfileQuality.SaturatedCounter);
        await Assert.That(counter).IsEqualTo(-1);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task ConcurrentIncrementLossIsBoundedAndAlwaysMarked(int maximumCasAttempts)
    {
        long counter = 0;
        long deliveredReturns = 0;
        long droppedReturns = 0;
        var qualityBits = 0;
        Parallel.For(0, ContentionAttempts, _ =>
        {
            var quality = DatabasePhaseArithmetic.TryIncrement(ref counter, maximumCasAttempts);
            _ = Interlocked.Or(ref qualityBits, (int)quality);
            if (quality == DatabaseProfileQuality.None)
            {
                Interlocked.Increment(ref deliveredReturns);
            }
            else if (quality == DatabaseProfileQuality.ContentionDropped)
            {
                Interlocked.Increment(ref droppedReturns);
            }
        });

        var delivered = Interlocked.Read(ref counter);
        var dropped = ContentionAttempts - delivered;
        var qualityResult = (DatabaseProfileQuality)Volatile.Read(ref qualityBits);
        await Assert.That(delivered).IsGreaterThan(0L);
        await Assert.That(delivered).IsLessThanOrEqualTo(ContentionAttempts);
        await Assert.That(dropped).IsGreaterThanOrEqualTo(0L);
        await Assert.That(delivered).IsEqualTo(deliveredReturns);
        await Assert.That(dropped).IsEqualTo(droppedReturns);
        await Assert.That(deliveredReturns + droppedReturns).IsEqualTo(ContentionAttempts);
        await Assert.That(dropped == 0 || qualityResult.HasFlag(DatabaseProfileQuality.ContentionDropped)).IsTrue();
        await Assert.That((qualityResult & DatabaseProfileQuality.SaturatedCounter) == 0).IsTrue();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task GenuineConcurrentIncrementsAtSaturationRetainEveryOriginalOutcome(int maximumCasAttempts)
    {
        var counter = long.MaxValue - 1;
        long saturatedReturns = 0;
        long droppedReturns = 0;
        Parallel.For(0, ContentionAttempts, _ =>
        {
            var quality = DatabasePhaseArithmetic.TryIncrement(ref counter, maximumCasAttempts);
            if (quality == DatabaseProfileQuality.SaturatedCounter)
            {
                Interlocked.Increment(ref saturatedReturns);
            }
            else if (quality == DatabaseProfileQuality.ContentionDropped)
            {
                Interlocked.Increment(ref droppedReturns);
            }
        });

        await Assert.That(Interlocked.Read(ref counter)).IsEqualTo(long.MaxValue);
        await Assert.That(saturatedReturns).IsGreaterThan(0L);
        await Assert.That(saturatedReturns + droppedReturns).IsEqualTo(ContentionAttempts);
        await Assert.That(DatabasePhaseArithmetic.TryIncrement(ref counter, maximumCasAttempts))
            .IsEqualTo(DatabaseProfileQuality.SaturatedCounter);
        await Assert.That(counter).IsEqualTo(long.MaxValue);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(5)]
    public async Task InvalidRetryOperandsRejectBeforeTouchingTheCounter(int maximumCasAttempts)
    {
        long counter = 42;
        var failure = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DatabasePhaseArithmetic.TryIncrement(ref counter, maximumCasAttempts));

        await Assert.That(failure.ParamName).IsEqualTo(nameof(maximumCasAttempts));
        await Assert.That(counter).IsEqualTo(42L);
    }

    [Test]
    public async Task SaturatingSnapshotAdditionHandlesExactAndOverflowEdges()
    {
        (long Left, long Right, long Expected, bool Overflow)[] cases =
        [
            (0, 0, 0, false),
            (20, 22, 42, false),
            (long.MaxValue - 1, 1, long.MaxValue, false),
            (long.MaxValue, 1, long.MaxValue, true),
            (long.MaxValue, long.MaxValue, long.MaxValue, true)
        ];

        foreach (var (left, right, expected, expectedOverflow) in cases)
        {
            var actual = DatabasePhaseArithmetic.AddSaturating(left, right, out var overflow);
            await Assert.That(actual).IsEqualTo(expected);
            await Assert.That(overflow).IsEqualTo(expectedOverflow);
        }
    }

    [Test]
    public async Task SaturatingSnapshotAdditionRejectsNegativeInputsBeforeAdding()
    {
        var negativeLeft = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => DatabasePhaseArithmetic.AddSaturating(-1, 0, out _));
        var negativeRight = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => DatabasePhaseArithmetic.AddSaturating(0, -1, out _));

        await Assert.That(negativeLeft).IsNotNull();
        await Assert.That(negativeRight).IsNotNull();
    }
}
