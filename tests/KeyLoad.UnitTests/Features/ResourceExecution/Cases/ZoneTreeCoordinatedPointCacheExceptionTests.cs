using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreeCoordinatedPointCacheExceptionTests
{
    private const string BorrowFailure = "The borrowed value changed after retirement.";
    private const string ReaderFailure = "The real borrowed reader failed after retirement.";
    private static readonly byte[] Key = "cache/coordinated/pinned"u8.ToArray();
    private static readonly byte[] Value = [3, 6, 9, 12];

    [Test]
    public async Task RetiredBorrowerExceptionReleasesItsPinAndHealthyNativeReadStillWorks()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out var grant, out var receipt);
            var store = fixture.OpenStore();
            ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
            var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
            await Assert.That(control.TryApply(receipt)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            _ = store.Read(view => view.ReadOwnedValue(Key));
            var failures = await RunRetiredBorrowerFailureAsync(store, control, permit, grant, receipt);
            await AssertFailureReleasedPinAsync(control, store, fixture, failures);
        });
    }

    private static async Task<List<Exception>> RunRetiredBorrowerFailureAsync(ZoneTreeStore store,
        ZoneTreePointCacheControl control, CacheReadPermit permit, Guid grant,
        CacheReadPermitAcceptance receipt)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        Task<bool>? borrower = null;
        try
        {
            await ZoneTreeCoordinatedPointCacheTestSupport.CollectFailureAsync(async () =>
            {
                borrower = ZoneTreeCoordinatedPointCacheTestSupport.StartLongRunning(() => store.Read(view =>
                    view.ReadValue(Key, value =>
                    {
                        entered.TrySetResult();
                        release.Task.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit)
                            .GetAwaiter().GetResult();
                        if (!value.SequenceEqual(Value))
                        {
                            throw new InvalidOperationException(BorrowFailure);
                        }
                        throw new InvalidOperationException(ReaderFailure);
                    })));
                await entered.Task.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit);
                await Assert.That(permit.TryWithdraw(grant, receipt.Revision)).IsTrue();
                await Assert.That(control.Retire(receipt.Revision)).IsTrue();
            }, failures);
        }
        finally
        {
            release.TrySetResult();
            if (borrower is not null)
            {
                await ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(borrower, failures);
            }
        }

        return failures;
    }

    private static async Task AssertFailureReleasedPinAsync(ZoneTreePointCacheControl control,
        ZoneTreeStore store, ZoneTreeCoordinatedPointCacheFileFixture fixture, List<Exception> failures)
    {
        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Message).IsEqualTo(ReaderFailure);
        var snapshot = control.GetDiagnostics();
        var healthy = store.Read(view => view.ReadOwnedValue(Key));
        await Assert.That(snapshot.ActivePins).IsEqualTo(0);
        await Assert.That(snapshot.RetainedBytes).IsEqualTo(fixture.CreateOptions().IndexChargeBytes);
        await Assert.That(healthy).IsNotNull();
        await Assert.That(healthy!.SequenceEqual(Value)).IsTrue();
        await Assert.That(control.GetDiagnostics().NativeLookups).IsEqualTo(2L);
    }
}
