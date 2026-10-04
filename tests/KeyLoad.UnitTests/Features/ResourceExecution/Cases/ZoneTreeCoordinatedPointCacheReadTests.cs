using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreeCoordinatedPointCacheReadTests
{
    private const string OwnedResultFailure = "The real ZoneTree read returned no owned result.";
    private const byte MutatedFirstByte = 77;
    private static readonly byte[] Key = "cache/coordinated/value"u8.ToArray();
    private static readonly byte[] Value = [0, 9, 128, 255];

    [Test]
    public async Task ReadyReceiptIsIdempotentAndWarmOwnedReadsReturnIndependentCopies()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out var receipt);
            var store = fixture.OpenStore();
            ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
            var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);

            var applied = control.TryApply(receipt);
            var first = store.Read(view => view.ReadOwnedValue(Key));
            var afterCold = control.GetDiagnostics();
            var repeated = control.TryApply(receipt);
            var afterIdempotentApply = control.GetDiagnostics();
            await Assert.That(first).IsNotNull();
            var ownedFirst = first ?? throw new InvalidOperationException(OwnedResultFailure);
            ownedFirst[0] = MutatedFirstByte;
            var warm = store.Read(view => view.ReadOwnedValue(Key.ToArray()));
            var afterWarm = control.GetDiagnostics();

            await Assert.That(applied).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            await Assert.That(repeated).IsEqualTo(ZoneTreePointCacheControlResult.AlreadyApplied);
            await Assert.That(ownedFirst[0]).IsEqualTo(MutatedFirstByte);
            await Assert.That(warm).IsNotNull();
            await Assert.That(warm!.SequenceEqual(Value)).IsTrue();
            await Assert.That(afterCold.Admissions).IsEqualTo(1L);
            await Assert.That(afterCold.NativeLookups).IsEqualTo(1L);
            await Assert.That(afterIdempotentApply.Admissions).IsEqualTo(afterCold.Admissions);
            await Assert.That(afterIdempotentApply.RetainedBytes).IsEqualTo(afterCold.RetainedBytes);
            await Assert.That(afterIdempotentApply.NativeLookups).IsEqualTo(afterCold.NativeLookups);
            await Assert.That(afterWarm.Hits).IsEqualTo(1L);
            await Assert.That(afterWarm.NativeLookups).IsEqualTo(1L);
            await Assert.That(afterWarm.RetainedBytes).IsEqualTo(afterCold.RetainedBytes);
        });
    }

    [Test]
    public async Task GenuineContinuousRenewalKeepsTheWarmHelperAndCounters()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out var initial);
            var store = fixture.OpenStore();
            ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
            var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
            await Assert.That(control.TryApply(initial)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            _ = store.Read(view => view.ReadOwnedValue(Key));
            _ = store.Read(view => view.ReadOwnedValue(Key));
            var before = control.GetDiagnostics();
            var renewal = ZoneTreeCoordinatedPointCacheTestSupport.AcceptRenewal(permit, 2, out _);

            var result = control.TryApply(renewal);
            var after = control.GetDiagnostics();
            var warm = store.Read(view => view.ReadOwnedValue(Key));

            await Assert.That(renewal).IsEqualTo(new CacheReadPermitAcceptance(2, 1, true));
            await Assert.That(result).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            await Assert.That(after.Enabled).IsTrue();
            await Assert.That(after.Hits).IsEqualTo(before.Hits);
            await Assert.That(after.NativeLookups).IsEqualTo(before.NativeLookups);
            await Assert.That(after.RetainedBytes).IsEqualTo(before.RetainedBytes);
            await Assert.That(warm).IsNotNull();
            await Assert.That(warm!.SequenceEqual(Value)).IsTrue();
            await Assert.That(control.GetDiagnostics().Hits).IsEqualTo(before.Hits + 1);
        });
    }

    [Test]
    public async Task ExpiredWarmReceiptFallsBackToOneNativeReadWithIdenticalBytes()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), TimeProvider.System);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            using var permit = new CacheReadPermit(TimeProvider.System);
            var clock = TimeProvider.System;
            var prepared = clock.GetTimestamp();
            await Task.Delay(TimeSpan.FromSeconds(2), clock, linked.Token);
            var beforeAccept = clock.GetTimestamp();
            await Assert.That(permit.TryAccept(Guid.NewGuid(), 1, prepared, out var receipt)).IsTrue();
            var store = fixture.OpenStore();
            ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
            var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
            await Assert.That(control.TryApply(receipt)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            _ = store.Read(view => view.ReadOwnedValue(Key));
            var warm = control.GetDiagnostics();
            await WaitUntilPreparedLeaseEndsAsync(clock, prepared, linked.Token);

            var acceptanceCurrent = permit.IsCurrentAcceptance(receipt);
            var captured = permit.TryCapture(out var expiredRevision);
            var expiredValue = store.Read(view => view.ReadOwnedValue(Key));
            var after = control.GetDiagnostics();
            var completed = clock.GetTimestamp();

            await Assert.That(clock.GetElapsedTime(prepared, completed) >= CacheReadPermitLimits.LeaseValidity).IsTrue();
            await Assert.That(clock.GetElapsedTime(beforeAccept, completed) < CacheReadPermitLimits.LeaseValidity).IsTrue();
            await Assert.That(acceptanceCurrent).IsFalse();
            await Assert.That(captured).IsFalse();
            await Assert.That(expiredRevision).IsEqualTo(0);
            await Assert.That(expiredValue).IsNotNull();
            await Assert.That(expiredValue!.SequenceEqual(Value)).IsTrue();
            await Assert.That(after.NativeLookups).IsEqualTo(warm.NativeLookups + 1);
            await Assert.That(after.Hits).IsEqualTo(warm.Hits);
            await Assert.That(after.Enabled).IsFalse();
        });
    }

    private static async Task WaitUntilPreparedLeaseEndsAsync(TimeProvider clock, long prepared,
        CancellationToken cancellationToken)
    {
        var currentAge = clock.GetElapsedTime(prepared, clock.GetTimestamp());
        var remaining = CacheReadPermitLimits.LeaseValidity + TimeSpan.FromMilliseconds(50) - currentAge;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, clock, cancellationToken);
        }
    }
}
