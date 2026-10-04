using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreeCoordinatedPointCacheRetirementTests
{
    private static readonly byte[] Key = "cache/coordinated/binding"u8.ToArray();
    private static readonly byte[] Value = [4, 7, 11];

    private readonly record struct ReplacementEvidence(bool WithdrewOld, bool RetiredOld,
        bool StaleWithdrawal, CacheReadPermitAcceptance Renewal, Guid RenewalGrant,
        ZoneTreePointCacheControlResult ApplyResult, ZoneTreePointCacheSnapshot Cold,
        byte[]? Refill, ZoneTreePointCacheSnapshot AfterRefill, byte[]? FirstWarm,
        ZoneTreePointCacheSnapshot BeforeStaleRetirement);

    [Test]
    public async Task DelayedOldRetirementCannotDisableNewerBinding()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out var oldGrant, out var initial);
            var store = fixture.OpenStore();
            ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
            var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
            await Assert.That(control.TryApply(initial)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            _ = store.Read(view => view.ReadOwnedValue(Key));
            var evidence = BindAfterOldRetirement(store, permit, control, oldGrant, initial);

            await AssertReplacementEvidenceAsync(evidence);
            await AssertDelayedStaleRetirementAsync(store, permit, control, initial, evidence);
        });
    }

    private static ReplacementEvidence BindAfterOldRetirement(ZoneTreeStore store, CacheReadPermit permit,
        ZoneTreePointCacheControl control, Guid oldGrant, CacheReadPermitAcceptance initial)
    {
        var withdrewOld = permit.TryWithdraw(oldGrant, initial.Revision);
        var retiredOld = control.Retire(initial.Revision);
        var renewal = ZoneTreeCoordinatedPointCacheTestSupport.AcceptRenewal(permit, 2, out var renewalGrant);
        var staleWithdrawal = permit.TryWithdraw(oldGrant, initial.Revision);
        var applyResult = control.TryApply(renewal);
        var cold = control.GetDiagnostics();
        var refill = store.Read(view => view.ReadOwnedValue(Key));
        var afterRefill = control.GetDiagnostics();
        var firstWarm = store.Read(view => view.ReadOwnedValue(Key));
        var beforeStaleRetirement = control.GetDiagnostics();
        return new ReplacementEvidence(withdrewOld, retiredOld, staleWithdrawal, renewal,
            renewalGrant, applyResult, cold, refill, afterRefill, firstWarm, beforeStaleRetirement);
    }

    private static async Task AssertReplacementEvidenceAsync(ReplacementEvidence evidence)
    {
        await Assert.That(evidence.WithdrewOld).IsTrue();
        await Assert.That(evidence.RetiredOld).IsTrue();
        await Assert.That(evidence.StaleWithdrawal).IsFalse();
        await Assert.That(evidence.Renewal).IsEqualTo(new CacheReadPermitAcceptance(2, 1, false));
        await Assert.That(evidence.ApplyResult).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
        await Assert.That(evidence.Cold.Hits).IsEqualTo(0L);
        await Assert.That(evidence.Cold.NativeLookups).IsEqualTo(0L);
        await Assert.That(evidence.Refill).IsNotNull();
        await Assert.That(evidence.Refill!.SequenceEqual(Value)).IsTrue();
        await Assert.That(evidence.AfterRefill.NativeLookups).IsEqualTo(1L);
        await Assert.That(evidence.FirstWarm).IsNotNull();
        await Assert.That(evidence.FirstWarm!.SequenceEqual(Value)).IsTrue();
    }

    private static async Task AssertDelayedStaleRetirementAsync(ZoneTreeStore store, CacheReadPermit permit,
        ZoneTreePointCacheControl control, CacheReadPermitAcceptance initial, ReplacementEvidence evidence)
    {
        var staleRetirement = control.Retire(initial.Revision);
        var afterStaleRetirement = control.GetDiagnostics();
        var warm = store.Read(view => view.ReadOwnedValue(Key));
        var afterWarm = control.GetDiagnostics();

        await Assert.That(staleRetirement).IsFalse();
        await Assert.That(afterStaleRetirement.Enabled).IsTrue();
        await Assert.That(afterStaleRetirement.RetainedBytes).IsEqualTo(evidence.BeforeStaleRetirement.RetainedBytes);
        await Assert.That(warm).IsNotNull();
        await Assert.That(warm!.SequenceEqual(Value)).IsTrue();
        await Assert.That(afterWarm.Hits).IsEqualTo(evidence.BeforeStaleRetirement.Hits + 1);
        var withdrewRenewal = permit.TryWithdraw(evidence.RenewalGrant, evidence.Renewal.Revision);
        await Assert.That(withdrewRenewal).IsTrue();
        await Assert.That(control.Retire(evidence.Renewal.Revision)).IsTrue();
        await Assert.That(control.Retire(evidence.Renewal.Revision)).IsFalse();
        await Assert.That(control.GetDiagnostics().Enabled).IsFalse();
    }
}
