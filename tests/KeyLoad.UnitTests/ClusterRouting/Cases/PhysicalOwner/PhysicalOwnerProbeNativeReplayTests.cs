using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeNativeReplayTests
{
    [Test]
    public async Task UtcExpiryAloneCannotReleaseCapacityOrOriginalNonce()
    {
        var clock = new ControlledReadClock(PhysicalOwnerProbeNativeTestValues.Now);
        using var replay = Create(clock);
        var nonce = PhysicalOwnerProbeNativeTestValues.InnerNonce();
        var expiry = clock.GetUtcNow() + PhysicalOwnerProbeNativeTestValues.Retention;
        await Assert.That(replay.TryUse(nonce, expiry)).IsTrue();
        clock.MoveUtc(expiry);
        await Assert.That(replay.TryUse(nonce, expiry)).IsFalse();
        await Assert.That(replay.TryUse(PhysicalOwnerProbeNativeTestValues.InnerNonce(), expiry)).IsFalse();
        clock.Advance(PhysicalOwnerProbeNativeTestValues.Retention - TimeSpan.FromTicks(1));
        await Assert.That(replay.TryUse(nonce, expiry)).IsFalse();
        clock.Advance(TimeSpan.FromTicks(1));
        await Assert.That(replay.TryUse(nonce, clock.GetUtcNow() + PhysicalOwnerProbeNativeTestValues.Retention)).IsTrue();
    }

    [Test]
    public async Task MonotonicRetentionAloneCannotReleaseEntryBeforeSignedExpiry()
    {
        var clock = new ControlledReadClock(PhysicalOwnerProbeNativeTestValues.Now);
        using var replay = Create(clock);
        var nonce = Guid.NewGuid().ToString("N");
        var expiry = clock.GetUtcNow() + PhysicalOwnerProbeNativeTestValues.Retention + TimeSpan.FromSeconds(1);
        await Assert.That(replay.TryUse(nonce, expiry)).IsTrue();
        clock.Advance(PhysicalOwnerProbeNativeTestValues.Retention);
        await Assert.That(replay.TryUse(Guid.NewGuid().ToString("N"), expiry)).IsFalse();
        clock.MoveUtc(expiry.AddTicks(-1));
        await Assert.That(replay.TryUse(nonce, expiry)).IsFalse();
        clock.MoveUtc(expiry);
        await Assert.That(replay.TryUse(Guid.NewGuid().ToString("N"), expiry + PhysicalOwnerProbeNativeTestValues.Retention)).IsTrue();
    }

    [Test]
    public async Task OriginalAndOuterNonceOwnersRemainSeparateAndBounded()
    {
        var clock = new ControlledReadClock(PhysicalOwnerProbeNativeTestValues.Now);
        using var original = Create(clock);
        using var outer = Create(clock);
        var innerNonce = PhysicalOwnerProbeNativeTestValues.InnerNonce();
        var outerNonce = Guid.NewGuid().ToString("N");
        var expiry = clock.GetUtcNow() + PhysicalOwnerProbeNativeTestValues.Retention;
        await Assert.That(original.TryUse(innerNonce, expiry)).IsTrue();
        await Assert.That(outer.TryUse(outerNonce, expiry)).IsTrue();
        await Assert.That(original.TryUse(innerNonce, expiry)).IsFalse();
        await Assert.That(outer.TryUse(outerNonce, expiry)).IsFalse();
        await Assert.That(PhysicalOwnerProbeNativeIdentity.ValidNonce(outerNonce)).IsFalse();
        await Assert.That(PhysicalOwnerProbeNativeIdentity.ValidNonce(innerNonce + "==")).IsFalse();
        await Assert.That(PhysicalOwnerProbeNativeIdentity.ValidNonce(new string('A', 43))).IsFalse();
        await Assert.That(PhysicalOwnerProbeNativeIdentity.ValidNonce(new string('A', 21) + "B")).IsFalse();
    }

    private static PhysicalOwnerProbeNativeReplay Create(ControlledReadClock clock)
        => new(clock, Options.Create(new OrleansMembershipOptions
        {
            ReplayNonceCapacity = 1,
            ReplayLifetime = PhysicalOwnerProbeNativeTestValues.Retention
        }));
}
