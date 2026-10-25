using KeyLoad.FunctionalTests.StorageRecovery.Assertions;
using static KeyLoad.FunctionalTests.StorageRecovery.CapturedReadScopeScenario;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class CapturedReadScopeTests
{
    [Test]
    public async Task AcCut001003CapturedScopesKeepOriginalBytesAfterLeaseAndStoreClose()
    {
        using var fixture = new ReadCutFixture();
        Seed(fixture.Store);
        var identity = ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        using var lease = fixture.Capture(ReadCutFixture.Limits(MaximumRecords, MaximumBytes));
        var originalPosition = fixture.Store.Position;
        Change(fixture.Store);
        using var frozen = Capture(lease);
        await RequireCutAsync(lease.Cut, identity, originalPosition);
        lease.Dispose();
        fixture.Reopen();
        var changedPosition = originalPosition + 1;
        await RequireCurrentAsync(fixture.Store, identity, changedPosition, changed: true, healthy: false);
        await RequireFrozenAsync(frozen);

        var lookup = Assert.ThrowsExactly<KeyLoadException>(() =>
            frozen.ReadOwnedValue(ReadCutFixture.Key(Unknown)));
        await Assert.That(lookup.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        var range = Assert.ThrowsExactly<KeyLoadException>(() =>
            frozen.VisitRange(ReadCutFixture.Key(string.Empty), MaximumRecords, static (_, _) => true));
        await Assert.That(range.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await RequireCurrentAsync(fixture.Store, identity, changedPosition, changed: true, healthy: false);
        await RequireFrozenAsync(frozen);

        using (var next = fixture.Capture(ReadCutFixture.Limits(MaximumRecords, MaximumBytes)))
        {
            byte[]? current = null;
            await Assert.That(next.ReadExact(ReadCutFixture.Key(Point),
                value => current = value.ToArray(), static (_, _) => { })).IsTrue();
            await Assert.That(current!.SequenceEqual(ReadCutFixture.Value(NewPoint))).IsTrue();
        }
        CommitHealthy(fixture.Store);
        fixture.Reopen();
        await RequireCurrentAsync(fixture.Store, identity, changedPosition + 1, changed: true, healthy: true);
        await RequireFrozenAsync(frozen);
        frozen.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => frozen.ReadOwnedValue(ReadCutFixture.Key(Point)));
        await RequireCurrentAsync(fixture.Store, identity, changedPosition + 1, changed: true, healthy: true);
    }

    [Test]
    public async Task AcCut002ExactPointByteRefusalPreventsDeliveryAndReleasesSlot()
    {
        using var fixture = new ReadCutFixture();
        Seed(fixture.Store);
        var identity = ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        var position = fixture.Store.Position;
        var key = ReadCutFixture.Key(Point);
        var value = ReadCutFixture.Value(OldPoint);
        var exactBytes = key.Length * 2L + value.Length + RuntimeHeaderBytes;
        using (var rejected = fixture.Capture(ReadCutFixture.Limits(MaximumRecords, exactBytes - 1)))
        {
            var delivered = 0;
            var admittedRecords = 0;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => rejected.ReadExact(key,
                _ => delivered++, (_, count) => admittedRecords += count));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(delivered).IsEqualTo(0);
            await Assert.That(admittedRecords).IsEqualTo(0);
            await RequireCurrentAsync(fixture.Store, identity, position, changed: false, healthy: false);
        }
        using (var accepted = fixture.Capture(ReadCutFixture.Limits(MaximumRecords, exactBytes)))
        {
            await RequireCutAsync(accepted.Cut, identity, position);
            byte[]? captured = null;
            var admittedRecords = 0;
            await Assert.That(accepted.ReadExact(key, current => captured = current.ToArray(),
                (_, count) => admittedRecords += count)).IsTrue();
            await Assert.That(captured!.SequenceEqual(value)).IsTrue();
            await Assert.That(admittedRecords).IsEqualTo(1);
            var settled = accepted.ObserveSettledWork();
            await Assert.That(settled.ExaminedBytes).IsEqualTo(exactBytes);
            await Assert.That(settled.Records).IsEqualTo(1);
        }
        fixture.Reopen();
        await RequireCurrentAsync(fixture.Store, identity, position, changed: false, healthy: false);
        CommitHealthy(fixture.Store);
        fixture.Reopen();
        await RequireCurrentAsync(fixture.Store, identity, position + 1, changed: false, healthy: true);
    }
}
