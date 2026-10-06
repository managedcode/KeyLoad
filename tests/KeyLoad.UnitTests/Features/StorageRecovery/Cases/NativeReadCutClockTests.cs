using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>AC-TIME-002: real native snapshot expiry follows its explicit monotonic provider.</summary>
internal sealed class NativeReadCutClockTests
{
    private const string Entity = "timed";
    private const string Value = "persisted";
    private static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(1);

    [Test]
    public async Task ExpiredNativeLeaseReleasesItsSlotAndPreservesOriginalCommittedBytes()
    {
        var clock = new ControlledReadClock(Epoch);
        using var fixture = new NativeReadCutFixture(clock);
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Put(NativeReadCutFixture.Key(Entity), NativeReadCutFixture.Value(Value));
            return true;
        });
        var before = fixture.Store.Position;
        var limits = NativeReadCutFixture.Limits(1, 1024, Lifetime);
        var lease = fixture.Capture(limits);
        KeyLoadException? disposalError = null;
        try
        {
            clock.MoveUtc(Epoch.AddYears(1));
            await Assert.That(lease.VisitPrefix(NativeReadCutFixture.Key(Entity), static (_, _) => false).Records).IsEqualTo(1);
            clock.Advance(Lifetime.Add(TimeSpan.FromTicks(1)));
            var error = Assert.ThrowsExactly<KeyLoadException>(() => lease.VisitPrefix(
                NativeReadCutFixture.Key(Entity), static (_, _) => false));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        }
        finally
        {
            try
            { lease.Dispose(); }
            catch (KeyLoadException error) when (error.Code == ErrorCode.BudgetExceeded) { disposalError = error; }
        }
        await Assert.That(disposalError).IsNotNull();
        await Assert.That(fixture.Store.Position).IsEqualTo(before);
        using var next = fixture.Capture(limits);
        byte[]? actual = null;
        var result = next.VisitPrefix(NativeReadCutFixture.Key(Entity), (_, value) =>
        {
            actual = value.ToArray();
            return false;
        });
        await Assert.That(result.Records).IsEqualTo(1);
        await Assert.That(actual).IsEquivalentTo(NativeReadCutFixture.Value(Value));
    }
}
