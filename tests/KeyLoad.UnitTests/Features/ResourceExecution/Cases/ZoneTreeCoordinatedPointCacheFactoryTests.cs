using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreeCoordinatedPointCacheFactoryTests
{
    private const string PermitFailure = "A fresh real-clock cache permit was rejected.";
    private const string ReaderFailure = "The held real store reader was not created.";
    private const string ControlFailure = "A created cache control was unexpectedly null.";
    private static readonly byte[] ReaderKey = "cache/coordinated/busy"u8.ToArray();
    private static readonly byte[] ReaderValue = [6, 12, 24];

    [Test]
    public async Task CreatedControlIsConfiguredColdAndDoesNotReserveAnIndex()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = AcceptedPermit(out _);
            var store = fixture.OpenStore();
            var before = fixture.Budget.GetSnapshot();

            var result = store.TryCreateCoordinatedPointCache(fixture.CreateOptions(), permit, out var control);
            var after = fixture.Budget.GetSnapshot();

            await Assert.That(result).IsEqualTo(ZoneTreePointCacheControlResult.Created);
            await Assert.That(control).IsNotNull();
            var createdControl = control ?? throw new InvalidOperationException(ControlFailure);
            var cold = createdControl.GetDiagnostics();
            await Assert.That(after.RetainedBytes).IsEqualTo(before.RetainedBytes);
            await Assert.That(after.RetainedEntries).IsEqualTo(before.RetainedEntries);
            await Assert.That(cold.Configured).IsTrue();
            await Assert.That(cold.Enabled).IsFalse();
            await Assert.That(cold.Closed).IsFalse();
            await Assert.That(cold.RetainedBytes).IsEqualTo(0L);
            await Assert.That(cold.ChargedEntries).IsEqualTo(0);
            await Assert.That(cold.LiveEntries).IsEqualTo(0);
            await Assert.That(cold.RetiredPinnedEntries).IsEqualTo(0);
            await Assert.That(cold.InFlightEntries).IsEqualTo(0);
            await Assert.That(cold.ActivePins).IsEqualTo(0);
            await Assert.That(cold.Hits).IsEqualTo(0L);
            await Assert.That(cold.Misses).IsEqualTo(0L);
            await Assert.That(cold.ReadBypasses).IsEqualTo(0L);
            await Assert.That(cold.NativeLookups).IsEqualTo(0L);
            await Assert.That(cold.NativeMisses).IsEqualTo(0L);
            await Assert.That(cold.AdmissionBypasses).IsEqualTo(0L);
            await Assert.That(cold.Admissions).IsEqualTo(0L);
            await Assert.That(cold.Evictions).IsEqualTo(0L);
            await Assert.That(cold.EvictionAttempts).IsEqualTo(0L);
        });
    }

    [Test]
    public async Task EmbeddedOrDuplicateFactoryCannotInstallAnotherOwner()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = AcceptedPermit(out _);
            var embedded = fixture.OpenStore(embedded: true);
            var store = fixture.OpenStore();
            var beforeEmbeddedFactory = fixture.Budget.GetSnapshot();
            var embeddedResult = embedded.TryCreateCoordinatedPointCache(fixture.CreateOptions(), permit, out var embeddedControl);
            var afterEmbeddedFactory = fixture.Budget.GetSnapshot();
            var created = store.TryCreateCoordinatedPointCache(fixture.CreateOptions(), permit, out var control);
            var duplicate = store.TryCreateCoordinatedPointCache(fixture.CreateOptions(), permit, out var duplicateControl);

            await Assert.That(embeddedResult).IsEqualTo(ZoneTreePointCacheControlResult.AlreadyConfigured);
            await Assert.That(embeddedControl).IsNull();
            await Assert.That(afterEmbeddedFactory.RetainedBytes).IsEqualTo(beforeEmbeddedFactory.RetainedBytes);
            await Assert.That(afterEmbeddedFactory.RetainedEntries).IsEqualTo(beforeEmbeddedFactory.RetainedEntries);
            await Assert.That(created).IsEqualTo(ZoneTreePointCacheControlResult.Created);
            await Assert.That(control).IsNotNull();
            await Assert.That(duplicate).IsEqualTo(ZoneTreePointCacheControlResult.AlreadyConfigured);
            await Assert.That(duplicateControl).IsNull();
            await Assert.That(fixture.Budget.GetSnapshot().RetainedBytes).IsEqualTo(afterEmbeddedFactory.RetainedBytes);
        });
    }

    [Test]
    public async Task FactoryAndApplyReturnBusyInsideRealStoreReadAndWriteCallbacks()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = AcceptedPermit(out var receipt);
            var store = fixture.OpenStore();
            await Assert.That(store.TryCreateCoordinatedPointCache(fixture.CreateOptions(), permit, out var control))
                .IsEqualTo(ZoneTreePointCacheControlResult.Created);

            var readResults = store.Read(_ =>
            {
                var factory = store.TryCreateCoordinatedPointCache(fixture.CreateOptions(), permit, out var ignored);
                return (factory, ignored, apply: control!.TryApply(receipt));
            });
            var writeResults = store.Commit((_, _) =>
            {
                var factory = store.TryCreateCoordinatedPointCache(fixture.CreateOptions(), permit, out var ignored);
                return (factory, ignored, apply: control!.TryApply(receipt));
            });

            await Assert.That(readResults.factory).IsEqualTo(ZoneTreePointCacheControlResult.Busy);
            await Assert.That(readResults.ignored).IsNull();
            await Assert.That(readResults.apply).IsEqualTo(ZoneTreePointCacheControlResult.Busy);
            await Assert.That(writeResults.factory).IsEqualTo(ZoneTreePointCacheControlResult.Busy);
            await Assert.That(writeResults.ignored).IsNull();
            await Assert.That(writeResults.apply).IsEqualTo(ZoneTreePointCacheControlResult.Busy);
        });
    }

    [Test]
    public async Task ClosedStoreRejectsControlCreationWithoutAllocatingAnIndex()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = AcceptedPermit(out _);
            var store = fixture.OpenStore();
            fixture.CloseStore(store);

            var result = store.TryCreateCoordinatedPointCache(fixture.CreateOptions(), permit, out var control);

            await Assert.That(result).IsEqualTo(ZoneTreePointCacheControlResult.Closed);
            await Assert.That(control).IsNull();
            await Assert.That(fixture.Budget.GetSnapshot().RetainedBytes).IsEqualTo(0L);
        });
    }

    [Test]
    public async Task CrossTaskHeldStoreReaderMakesApplyBusyThenAllowsSameReceiptRetry()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = AcceptedPermit(out var receipt);
            var store = fixture.OpenStore();
            ZoneTreeCoordinatedPointCacheTestSupport.Put(store, ReaderKey, ReaderValue);
            await Assert.That(store.TryCreateCoordinatedPointCache(fixture.CreateOptions(), permit, out var control))
                .IsEqualTo(ZoneTreePointCacheControlResult.Created);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failures = new List<Exception>();
            Task<byte[]?>? reader = null;
            try
            {
                await ZoneTreeCoordinatedPointCacheTestSupport.CollectFailureAsync(async () =>
                {
                    reader = ZoneTreeCoordinatedPointCacheTestSupport.StartLongRunning(() => store.Read(view =>
                    {
                        entered.TrySetResult();
                        release.Task.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit).GetAwaiter().GetResult();
                        return view.ReadOwnedValue(ReaderKey);
                    }));
                    await entered.Task.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit);
                    await Assert.That(control!.TryApply(receipt)).IsEqualTo(ZoneTreePointCacheControlResult.Busy);
                }, failures);
            }
            finally
            {
                release.TrySetResult();
                if (reader is not null)
                {
                    await ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(reader, failures);
                }
            }

            ZoneTreeCoordinatedPointCacheTestSupport.ThrowFailures(failures);
            var settledReader = reader ?? throw new InvalidOperationException(ReaderFailure);
            var value = await settledReader;
            await Assert.That(value).IsNotNull();
            await Assert.That(value!.SequenceEqual(ReaderValue)).IsTrue();
            await Assert.That(control!.TryApply(receipt)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
        });
    }

    private static CacheReadPermit AcceptedPermit(out CacheReadPermitAcceptance receipt)
    {
        var permit = new CacheReadPermit(TimeProvider.System);
        if (!permit.TryAccept(Guid.NewGuid(), 1, TimeProvider.System.GetTimestamp(), out receipt))
        {
            permit.Dispose();
            throw new InvalidOperationException(PermitFailure);
        }

        return permit;
    }
}
