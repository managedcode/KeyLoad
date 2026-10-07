using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeReadCutLifetimeTests
{
    private const string RecordId = "one";
    private const string RecordValue = "value";

    [Test]
    public async Task AcCut003RejectsConcurrentAndReentrantUseButAllowsFollowingLease()
    {
        using var fixture = new NativeReadCutFixture();
        fixture.Store.Commit((tx, _) => { tx.Put(NativeReadCutFixture.Key(RecordId), NativeReadCutFixture.Value(RecordValue)); return true; });
        using (var lease = fixture.Capture(NativeReadCutFixture.Limits(4, 128)))
        {
            var outer = lease.VisitPrefix(
                NativeReadCutFixture.Key(string.Empty), (_, _) =>
                {
                    var nested = Assert.ThrowsExactly<KeyLoadException>(() => lease.VisitPrefix(
                        NativeReadCutFixture.Key(string.Empty), static (_, _) => true));
                    if (nested.Code != ErrorCode.ResourceExhausted)
                    {
                        throw new InvalidOperationException("The reentrant traversal returned the wrong error code.");
                    }
                    return false;
                });
            await Assert.That(outer.StoppedByVisitor).IsTrue();
            await Assert.That(outer.Records).IsEqualTo(1);
        }

        using var next = fixture.Capture(NativeReadCutFixture.Limits(4, 128));
        var result = next.VisitPrefix(NativeReadCutFixture.Key(string.Empty), static (_, _) => true);
        await Assert.That(result.Records).IsEqualTo(1);
    }

    [Test]
    public async Task AcCut003ConcurrentTraversalFailsWithoutAdvancingTheSharedIterator()
    {
        using var fixture = new NativeReadCutFixture();
        fixture.Store.Commit((tx, _) => { tx.Put(NativeReadCutFixture.Key(RecordId), NativeReadCutFixture.Value(RecordValue)); return true; });
        using var lease = fixture.Capture(NativeReadCutFixture.Limits(4, 128));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = Task.Run(() => lease.VisitPrefix(NativeReadCutFixture.Key(string.Empty), (_, _) =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The owned traversal barrier expired.");
            }
            return true;
        }));
        var failures = new List<Exception>();
        try
        {
            if (!entered.Wait(TimeSpan.FromSeconds(10)))
            {
                failures.Add(new TimeoutException("The native traversal did not reach its owned barrier."));
            }
            else
            {
                await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    var concurrent = Assert.ThrowsExactly<KeyLoadException>(() => lease.VisitPrefix(
                        NativeReadCutFixture.Key(string.Empty), static (_, _) => true));
                    await Assert.That(concurrent.Code).IsEqualTo(ErrorCode.ResourceExhausted);
                }, failures);
            }
        }
        finally
        {
            release.Set();
        }
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await first;
        }, failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    [Test]
    public async Task AcCut003ShutdownCancelsAndJoinsTraversalBeforeReopen()
    {
        using var fixture = new NativeReadCutFixture();
        var originalKey = NativeReadCutFixture.Key(RecordId);
        var expectedOriginalValue = NativeReadCutFixture.Value(RecordValue);
        fixture.Store.Commit((tx, _) => { tx.Put(originalKey, expectedOriginalValue); return true; });
        var expectedIdentity = NativeReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        var originalPosition = fixture.Store.Position;
        var originalValue = fixture.Store.Read(view => view.ReadOwnedValue(originalKey))!.ToArray();
        await NativeReadCutStoreStateAssertions.AssertRecordAsync(fixture.Store, originalKey, expectedOriginalValue);
        var lease = fixture.Capture(NativeReadCutFixture.Limits(4, 128));
        using var entered = new ManualResetEventSlim();
        var traversal = Task.Run(() => lease.VisitPrefix(NativeReadCutFixture.Key(string.Empty), (_, _) =>
        {
            entered.Set();
            lease.CancellationToken.WaitHandle.WaitOne();
            lease.CancellationToken.ThrowIfCancellationRequested();
            return true;
        }));
        if (!entered.Wait(TimeSpan.FromSeconds(10)))
        {
            fixture.Store.Dispose();
            await ObserveTaskAsync(traversal);
            throw new InvalidOperationException("The native traversal did not enter its callback.");
        }

        var disposal = Task.Run(fixture.Store.Dispose);
        await JoinOriginalTasksAsync(traversal, disposal);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await traversal);
        await disposal;
        fixture.Reopen();
        await NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(fixture.Store, expectedIdentity, originalPosition);
        await NativeReadCutStoreStateAssertions.AssertRecordAsync(fixture.Store, originalKey, originalValue);

        var followUpKey = NativeReadCutFixture.Key("two");
        var followUpValue = NativeReadCutFixture.Value("healthy");
        var followUpPosition = fixture.Store.Commit((tx, proposedPosition) =>
        {
            tx.Put(followUpKey, followUpValue);
            return proposedPosition;
        });
        await Assert.That(followUpPosition).IsEqualTo(originalPosition + 1);
        await NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(fixture.Store, expectedIdentity, followUpPosition);
        await NativeReadCutStoreStateAssertions.AssertRecordAsync(fixture.Store, originalKey, originalValue);
        await NativeReadCutStoreStateAssertions.AssertRecordAsync(fixture.Store, followUpKey, followUpValue);

        fixture.Reopen();
        await NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(fixture.Store, expectedIdentity, followUpPosition);
        await NativeReadCutStoreStateAssertions.AssertRecordAsync(fixture.Store, originalKey, originalValue);
        await NativeReadCutStoreStateAssertions.AssertRecordAsync(fixture.Store, followUpKey, followUpValue);
        lease.Dispose();
    }

    [Test]
    public async Task AcCut003ReentrantStoreDisposalFailsWithoutClosingTheRuntime()
    {
        using var fixture = new NativeReadCutFixture();
        fixture.Store.Commit((tx, _) => { tx.Put(NativeReadCutFixture.Key(RecordId), NativeReadCutFixture.Value(RecordValue)); return true; });
        using var lease = fixture.Capture(NativeReadCutFixture.Limits(4, 128));
        var result = lease.VisitPrefix(NativeReadCutFixture.Key(string.Empty), (_, _) =>
        {
            var failure = Assert.ThrowsExactly<InvalidOperationException>(fixture.Store.Dispose);
            if (failure.Message.Length == 0)
            {
                throw new InvalidOperationException("The reentrant disposal error had no diagnostic.");
            }
            return false;
        });
        await Assert.That(result.StoppedByVisitor).IsTrue();
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(NativeReadCutFixture.Key(RecordId)) is not null)).IsTrue();
    }

    [Test]
    public async Task AcCut003StoreDisposeInsideReadGateLeavesLeaseAdmissionOpen()
    {
        using var fixture = new NativeReadCutFixture();
        fixture.Store.Commit((tx, _) => { tx.Put(NativeReadCutFixture.Key(RecordId), NativeReadCutFixture.Value(RecordValue)); return true; });
        var failure = Assert.ThrowsExactly<LockRecursionException>(() => fixture.Store.Read(_ =>
        {
            fixture.Store.Dispose();
            return true;
        }));
        await Assert.That(failure.Message.Length).IsGreaterThan(0);
        var writeFailure = Assert.ThrowsExactly<LockRecursionException>(() => fixture.Store.Commit((_, _) =>
        {
            fixture.Store.Dispose();
            return true;
        }));
        await Assert.That(writeFailure.Message.Length).IsGreaterThan(0);
        using var lease = fixture.Capture(NativeReadCutFixture.Limits(4, 128));
        var result = lease.VisitPrefix(NativeReadCutFixture.Key(string.Empty), static (_, _) => true);
        await Assert.That(result.Records).IsEqualTo(1);
    }

    private static async Task JoinOriginalTasksAsync(Task traversal, Task disposal)
    {
        var joined = Task.WhenAll(traversal, disposal);
        var timedOut = false;
        try
        {
            await joined.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System);
        }
        catch (TimeoutException)
        {
            timedOut = true;
            await ObserveTaskAsync(joined);
        }
        catch (OperationCanceledException)
        {
        }
        if (timedOut)
        {
            throw new InvalidOperationException("Native read-cut shutdown failed to join its original tasks.");
        }
    }

    private static async Task ObserveTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
