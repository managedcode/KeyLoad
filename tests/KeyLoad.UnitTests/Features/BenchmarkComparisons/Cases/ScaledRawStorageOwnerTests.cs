using System.Runtime.ExceptionServices;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

[NotInParallel]
internal sealed class ScaledRawStorageOwnerTests
{
    private const int MiniRecordCount = 4;
    private const int MiniValueBytes = 32;
    private const int FirstRecordIndex = 0;
    private const ulong FirstRecordId = 0UL;
    private const long OneNativeRead = 1L;

    [Test]
    public async Task AcScale003CompetingFixtureIsRejectedUntilTheOwnedFixtureCloses()
    {
        await AssertNoRetainedFailedOwnerAsync();
        await ScaledRawStorageTestLifetime.RunAsync(
            () => new ScaledRawStorageFixture(MiniRecordCount, MiniValueBytes), async first =>
            {
                var beforeCompetition = first.Capture();
                await AssertInitialSnapshotAsync(beforeCompetition);
                await AssertCompetingFixtureRejectedAsync();
                await AssertNoRetainedFailedOwnerAsync();

                var afterCompetition = first.Capture();
                await AssertUnchangedCountersAsync(beforeCompetition, afterCompetition);
                await AssertOneRealReadAsync(first, afterCompetition);

                first.Dispose();
                first.Dispose();
                await AssertNoRetainedFailedOwnerAsync();
            });

        await AssertNoRetainedFailedOwnerAsync();
        await ScaledRawStorageTestLifetime.RunAsync(
            () => new ScaledRawStorageFixture(MiniRecordCount, MiniValueBytes),
            AssertReplacementReadAsync);
        await AssertNoRetainedFailedOwnerAsync();
    }

    private static async Task AssertInitialSnapshotAsync(ScaledRawStorageSnapshot snapshot)
    {
        await Assert.That(snapshot.SeedAttempts).IsEqualTo(MiniRecordCount);
        await Assert.That(snapshot.SuccessfulSeedWrites).IsEqualTo(MiniRecordCount);
        await Assert.That(snapshot.NativeReadCalls).IsEqualTo(MiniRecordCount + 1L);
    }

    private static async Task AssertCompetingFixtureRejectedAsync()
    {
        ScaledRawStorageFixture? unexpectedFixture = null;
        Exception? bodyFailure = null;
        var bodyCompleted = false;
        var cleanupFailures = new List<Exception>();

        try
        {
            await Assert.That(() =>
            {
                unexpectedFixture = new ScaledRawStorageFixture(MiniRecordCount, MiniValueBytes);
            }).Throws<InvalidOperationException>();
            bodyCompleted = true;
        }
        catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
        {
            bodyFailure = failure;
            throw;
        }
        finally
        {
            CloseAfterBody(bodyCompleted, bodyFailure, unexpectedFixture, null, cleanupFailures);
        }
    }

    private static async Task AssertNoRetainedFailedOwnerAsync()
    {
        await Assert.That(ScaledRawStorageFixture.HasRetainedFailedOwner).IsFalse();
        await Assert.That(ScaledRawStorageFixture.RetryFailedOwnerClose()).IsFalse();
    }

    private static async Task AssertUnchangedCountersAsync(
        ScaledRawStorageSnapshot before,
        ScaledRawStorageSnapshot after)
    {
        await Assert.That(after.SeedAttempts).IsEqualTo(before.SeedAttempts);
        await Assert.That(after.SuccessfulSeedWrites).IsEqualTo(before.SuccessfulSeedWrites);
        await Assert.That(after.NativeReadCalls).IsEqualTo(before.NativeReadCalls);
    }

    private static async Task AssertOneRealReadAsync(
        ScaledRawStorageFixture fixture,
        ScaledRawStorageSnapshot beforeRead)
    {
        await Assert.That(fixture.Read(FirstRecordIndex)).IsEqualTo(FirstRecordId);
        var afterRead = fixture.Capture();
        await Assert.That(afterRead.SeedAttempts).IsEqualTo(beforeRead.SeedAttempts);
        await Assert.That(afterRead.SuccessfulSeedWrites).IsEqualTo(beforeRead.SuccessfulSeedWrites);
        await Assert.That(afterRead.NativeReadCalls - beforeRead.NativeReadCalls).IsEqualTo(OneNativeRead);
    }

    private static async Task AssertReplacementReadAsync(ScaledRawStorageFixture replacement)
    {
        var beforeRead = replacement.Capture();
        await Assert.That(beforeRead.SeedAttempts).IsEqualTo(MiniRecordCount);
        await Assert.That(beforeRead.SuccessfulSeedWrites).IsEqualTo(MiniRecordCount);
        await Assert.That(beforeRead.NativeReadCalls).IsEqualTo(MiniRecordCount + 1L);
        await Assert.That(replacement.Read(FirstRecordIndex)).IsEqualTo(FirstRecordId);
        var afterRead = replacement.Capture();
        await Assert.That(afterRead.NativeReadCalls - beforeRead.NativeReadCalls).IsEqualTo(OneNativeRead);
    }

    private static void CloseAfterBody(bool bodyCompleted, Exception? bodyFailure,
        ScaledRawStorageFixture? first, ScaledRawStorageFixture? replacement, List<Exception> failures)
    {
        if (bodyCompleted || bodyFailure is not null)
        {
            CollectDispose(replacement, failures);
            CollectDispose(first, failures);
            ThrowFailures(bodyFailure, failures);
        }
    }

    private static void CollectDispose(ScaledRawStorageFixture? fixture, List<Exception> failures)
    {
        if (fixture is null)
        {
            return;
        }

        try
        {
            fixture.Dispose();
        }
        catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

    private static void ThrowFailures(Exception? bodyFailure, List<Exception> cleanupFailures)
    {
        if (cleanupFailures.Count == 0)
        {
            return;
        }

        if (bodyFailure is null && cleanupFailures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
            return;
        }

        var failures = new List<Exception>(cleanupFailures.Count + (bodyFailure is null ? 0 : 1));
        if (bodyFailure is not null)
        {
            failures.Add(bodyFailure);
        }

        failures.AddRange(cleanupFailures);
        throw new AggregateException(failures);
    }
}
