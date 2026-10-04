using KeyLoad.Core.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedReservationTests
{
    [Test]
    public async Task ObservedOwnedPeakAndWorkReservationsAreInclusiveAndOneUnderRejects()
    {
        using var database = AnnSeedTestSupport.Create(33);
        var observed = AnnSeedTestSupport.Capture(database);
        var exact = new AnnSeedOptions
        {
            MaxOwnedBytes = observed.OwnedBytesUpperBound,
            MaxPeakBytes = observed.PeakBytesUpperBound,
            MaxWorkUnits = observed.WorkUnits
        };
        var accepted = AnnSeedTestSupport.Capture(database, options: exact);

        await Assert.That(accepted.Records.Length).IsEqualTo(observed.Records.Length);
        await Assert.That(accepted.OwnedBytesUpperBound).IsEqualTo(observed.OwnedBytesUpperBound);
        await Assert.That(accepted.PeakBytesUpperBound).IsEqualTo(observed.PeakBytesUpperBound);
        await Assert.That(accepted.WorkUnits).IsEqualTo(observed.WorkUnits);

        await AssertBudgetExceeded(database, exact with { MaxOwnedBytes = observed.OwnedBytesUpperBound - 1 });
        await AssertBudgetExceeded(database, exact with { MaxPeakBytes = observed.PeakBytesUpperBound - 1 });
        await AssertBudgetExceeded(database, exact with { MaxWorkUnits = observed.WorkUnits - 1 });

        var exactRecordLimit = AnnSeedTestSupport.Capture(database,
            options: new() { MaxRecords = observed.Records.Length });
        await Assert.That(exactRecordLimit.Records.Length).IsEqualTo(observed.Records.Length);
        await AssertBudgetExceeded(database, new() { MaxRecords = observed.Records.Length - 1 });
    }

    [Test]
    public async Task EmptySeedMatchesIndependentFrozenMemoryFormula()
    {
        using var database = AnnSeedTestSupport.Create(0);
        var seed = AnnSeedTestSupport.Capture(database);
        var scopeStrings = new[]
        {
            seed.Scope.PrincipalId,
            seed.Scope.Partition.TenantId,
            seed.Scope.Partition.DatabaseId,
            seed.Scope.Partition.TransactionDomainId,
            seed.Scope.Partition.PartitionKey,
            seed.Scope.Collection,
            seed.Scope.Field,
            seed.Scope.Space.Id,
            seed.Scope.Space.Model,
            seed.Scope.Space.Version
        };
        var expected = 2_048L + ArrayAllowance(1, 4_096) + 3 * 64L + 128L
            + StringAllowance(seed.CorpusSha256) + ArrayAllowance(8, 0);
        foreach (var value in scopeStrings)
        {
            expected = checked(expected + StringAllowance(value));
        }

        await Assert.That(ArrayAllowance(1, 4_096)).IsEqualTo(4_160L);
        await Assert.That(seed.Records.Length).IsEqualTo(0);
        await Assert.That(seed.OwnedBytesUpperBound).IsEqualTo(expected);
    }

    [Test]
    public async Task PeakReservationMustCoverOwnedReservation()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var failure = AnnSeedTestSupport.CaptureFailure(database, AnnSeedTestSupport.Principal,
            options: new() { MaxOwnedBytes = 4096, MaxPeakBytes = 2048 });

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
    }

    private static async Task AssertBudgetExceeded(TestDatabase database, AnnSeedOptions options)
    {
        var failure = AnnSeedTestSupport.CaptureFailure(database, AnnSeedTestSupport.Principal, options: options);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static long ArrayAllowance(int width, int length)
        => checked(64 + Align8(checked((long)width * length)));

    private static long StringAllowance(string value)
        => checked(64 + Align8(checked(2L * (value.Length + 1))));

    private static long Align8(long value) => checked((value + 7) & ~7L);
}
