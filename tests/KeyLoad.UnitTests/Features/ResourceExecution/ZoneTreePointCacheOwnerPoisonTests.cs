using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheOwnerPoisonTests
{
    private const string ApplyFailure = "Intentional real MutationApplied observer failure.";
    private static readonly byte[] Key = "cache/owner/poison"u8.ToArray();
    private static readonly byte[] InitialValue = [8, 16, 32];
    private static readonly byte[] DurableValue = [44, 55, 66];

    [Test]
    public async Task PoisonedApplyPreservesRecoveryErrorAndColdReopenBytes()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(() => VerifyPoisonAndRecoveryAsync(fixture));
    }

    private static async Task VerifyPoisonAndRecoveryAsync(ZoneTreeCoordinatedPointCacheFileFixture fixture)
    {
        var failApply = false;
        var store = fixture.OpenStore(faultObserver: (stage, _, _) =>
        {
            if (failApply && stage == CommitStage.MutationApplied)
            {
                throw new InvalidOperationException(ApplyFailure);
            }
        });
        using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out var receipt);
        ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, InitialValue);
        var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
        await Assert.That(control.TryApply(receipt)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
        _ = store.Read(view => view.ReadOwnedValue(Key));
        var beforeFailure = control.GetDiagnostics();

        failApply = true;
        var writeFailure = Assert.ThrowsExactly<KeyLoadException>(() => Put(store, DurableValue));
        var readFailure = Assert.ThrowsExactly<KeyLoadException>(() => store.Read(view => view.ReadOwnedValue(Key)));
        var failedIdentity = new ZoneTreePointCacheOwnerIdentity(store.Identity.NodeId,
            store.Identity.Incarnation, control.RuntimeId);
        var ownerFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            control.TryReadOwnerIdentity(out failedIdentity));

        await Assert.That(writeFailure.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(readFailure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(ownerFailure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(failedIdentity).IsEqualTo(default(ZoneTreePointCacheOwnerIdentity));
        await Assert.That(control.GetDiagnostics().Hits).IsEqualTo(beforeFailure.Hits);

        store.Dispose();
        await VerifyColdReopenAsync(fixture, store);
    }

    private static async Task VerifyColdReopenAsync(ZoneTreeCoordinatedPointCacheFileFixture fixture,
        ZoneTreeStore poisonedStore)
    {
        var reopened = fixture.ReopenStoreAtSameDirectory(poisonedStore);
        using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out _);
        var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(reopened, fixture, permit);
        var status = control.TryReadOwnerIdentity(out var identity);
        await Assert.That(status).IsEqualTo(ZoneTreePointCacheOwnerStatus.Healthy);
        await Assert.That(identity.NodeId).IsEqualTo(reopened.Identity.NodeId);
        await Assert.That(identity.Incarnation).IsEqualTo(reopened.Identity.Incarnation);
        await Assert.That(identity.RuntimeId).IsEqualTo(control.RuntimeId);

        var beforeRead = reopened.GetReadDiagnostics();
        var actual = reopened.Read(view => view.ReadOwnedValue(Key));
        var afterRead = reopened.GetReadDiagnostics();
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.SequenceEqual(DurableValue)).IsTrue();
        await Assert.That(afterRead.OwnedPointLookups).IsEqualTo(beforeRead.OwnedPointLookups + 1);
        await Assert.That(control.GetDiagnostics().Hits).IsEqualTo(0L);
    }

    private static void Put(ZoneTreeStore store, byte[] value)
        => store.Commit((transaction, _) => { transaction.Put(Key, value); return true; });
}
