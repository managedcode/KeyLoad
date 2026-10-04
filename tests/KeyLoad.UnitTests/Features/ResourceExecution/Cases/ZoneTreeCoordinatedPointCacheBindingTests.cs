using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreeCoordinatedPointCacheBindingTests
{
    private const string ObserverFailure = "The exact real receipt could not be withdrawn in the observer.";
    private static readonly byte[] Key = "cache/coordinated/binding"u8.ToArray();
    private static readonly byte[] Value = [4, 7, 11];
    [Test]
    public async Task SkippedContinuousPredecessorForcesColdReplacement()
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
            var old = control.GetDiagnostics();
            var acceptedButUnbound = ZoneTreeCoordinatedPointCacheTestSupport.AcceptRenewal(permit, 2, out _);
            var oldBindingRead = store.Read(view => view.ReadOwnedValue(Key));
            var afterOldBindingRead = control.GetDiagnostics();
            var skipped = ZoneTreeCoordinatedPointCacheTestSupport.AcceptRenewal(permit, 3, out _);

            var result = control.TryApply(skipped);
            var coldReplacement = control.GetDiagnostics();
            var first = store.Read(view => view.ReadOwnedValue(Key));
            var afterFill = control.GetDiagnostics();
            var second = store.Read(view => view.ReadOwnedValue(Key));

            await Assert.That(skipped).IsEqualTo(new CacheReadPermitAcceptance(3, 2, true));
            await Assert.That(acceptedButUnbound).IsEqualTo(new CacheReadPermitAcceptance(2, 1, true));
            await Assert.That(oldBindingRead).IsNotNull();
            await Assert.That(oldBindingRead!.SequenceEqual(Value)).IsTrue();
            await Assert.That(afterOldBindingRead.Enabled).IsFalse();
            await Assert.That(afterOldBindingRead.NativeLookups).IsEqualTo(old.NativeLookups + 1);
            await Assert.That(afterOldBindingRead.Hits).IsEqualTo(old.Hits);
            await Assert.That(result).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            await Assert.That(coldReplacement.Hits).IsEqualTo(0L);
            await Assert.That(coldReplacement.NativeLookups).IsEqualTo(0L);
            await Assert.That(coldReplacement.RetainedBytes).IsEqualTo(
                fixture.CreateOptions().IndexChargeBytes);
            await Assert.That(old.Hits).IsEqualTo(1L);
            await Assert.That(first).IsNotNull();
            await Assert.That(first!.SequenceEqual(Value)).IsTrue();
            await Assert.That(afterFill.NativeLookups).IsEqualTo(1L);
            await Assert.That(afterFill.Admissions).IsEqualTo(1L);
            await Assert.That(second).IsNotNull();
            await Assert.That(second!.SequenceEqual(Value)).IsTrue();
            await Assert.That(control.GetDiagnostics().Hits).IsEqualTo(1L);
        });
    }

    [Test]
    public async Task WithdrawnBusyRenewalCanRetireItsOlderUnboundBinding()
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
            var beforeRenewal = control.GetDiagnostics();
            var renewal = ZoneTreeCoordinatedPointCacheTestSupport.AcceptRenewal(permit, 2, out var renewalGrant);
            var busy = store.Read(_ => control.TryApply(renewal));
            var unboundValue = store.Read(view => view.ReadOwnedValue(Key));
            var afterUnboundRead = control.GetDiagnostics();
            var withdrewRenewal = permit.TryWithdraw(renewalGrant, renewal.Revision);
            var retiredOlderBinding = control.Retire(renewal.Revision);

            await Assert.That(busy).IsEqualTo(ZoneTreePointCacheControlResult.Busy);
            await Assert.That(unboundValue).IsNotNull();
            await Assert.That(unboundValue!.SequenceEqual(Value)).IsTrue();
            await Assert.That(afterUnboundRead.Enabled).IsFalse();
            await Assert.That(afterUnboundRead.NativeLookups).IsEqualTo(beforeRenewal.NativeLookups + 1);
            await Assert.That(afterUnboundRead.Hits).IsEqualTo(beforeRenewal.Hits);
            await Assert.That(withdrewRenewal).IsTrue();
            await Assert.That(retiredOlderBinding).IsTrue();
            await Assert.That(control.GetDiagnostics().Enabled).IsFalse();
        });
    }

    [Test]
    public async Task RetireDuringNativeObserverChargesAndLooksUpOnceWithoutPublishingFill()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out var grant, out var receipt);
            var store = fixture.OpenStore();
            ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
            var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
            await Assert.That(control.TryApply(receipt)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            var observerCalls = 0;
            long observedBytes = 0;
            byte[]? borrowedCopy = null;
            var logicalBefore = store.GetReadDiagnostics();

            var found = store.Read(view => view.ReadValue(Key, value => borrowedCopy = value.ToArray(), amount =>
            {
                observerCalls++;
                observedBytes += amount;
                if (!permit.TryWithdraw(grant, receipt.Revision)
                    || !control.Retire(receipt.Revision))
                {
                    throw new InvalidOperationException(ObserverFailure);
                }
            }));
            var afterObserver = control.GetDiagnostics();
            var logicalAfter = store.GetReadDiagnostics();

            await Assert.That(found).IsTrue();
            await Assert.That(borrowedCopy).IsNotNull();
            await Assert.That(borrowedCopy!.SequenceEqual(Value)).IsTrue();
            await Assert.That(observerCalls).IsEqualTo(1);
            await Assert.That(observedBytes).IsEqualTo((long)Key.Length + Value.Length);
            await Assert.That(afterObserver.NativeLookups).IsEqualTo(1L);
            await Assert.That(afterObserver.Admissions).IsEqualTo(0L);
            await Assert.That(afterObserver.InFlightEntries).IsEqualTo(0);
            await Assert.That(afterObserver.LiveEntries).IsEqualTo(0);
            await Assert.That(afterObserver.Hits).IsEqualTo(0L);
            await Assert.That(logicalAfter.BorrowedPointLookups - logicalBefore.BorrowedPointLookups).IsEqualTo(1L);
            await Assert.That(logicalAfter.PointExaminedBytes - logicalBefore.PointExaminedBytes)
                .IsEqualTo((long)Key.Length + Value.Length);
        });
    }

}
