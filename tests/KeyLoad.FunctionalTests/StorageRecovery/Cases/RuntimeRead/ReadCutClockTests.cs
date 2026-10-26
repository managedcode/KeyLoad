using KeyLoad.FunctionalTests.ResourceExecution;

namespace KeyLoad.FunctionalTests.StorageRecovery;

/// <summary>AC-TIME-002: real execution snapshot expiry follows its explicit monotonic provider.</summary>
internal sealed class ReadCutClockTests
{
    private const string Entity = "timed";
    private const string Value = "persisted";
    private static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(1);

    [Test]
    public async Task ExpiredLeaseReleasesItsSlotAndPreservesOriginalCommittedBytes()
    {
        var clock = new ControlledReadClock(Epoch);
        using var fixture = new ReadCutFixture(clock);
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Put(ReadCutFixture.Key(Entity), ReadCutFixture.Value(Value));
            return true;
        });
        var before = fixture.Store.Position;
        var limits = ReadCutFixture.Limits(1, 1024, Lifetime);
        var lease = fixture.Capture(limits);
        KeyLoadException? disposalError = null;
        try
        {
            clock.MoveUtc(Epoch.AddYears(1));
            await Assert.That(lease.VisitPrefix(ReadCutFixture.Key(Entity), static (_, _) => false).Records).IsEqualTo(1);
            clock.Advance(Lifetime.Add(TimeSpan.FromTicks(1)));
            var error = Assert.ThrowsExactly<KeyLoadException>(() => lease.VisitPrefix(
                ReadCutFixture.Key(Entity), static (_, _) => false));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        }
        finally
        {
            try
            {
                lease.Dispose();
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.BudgetExceeded) { disposalError = error; }
        }
        await Assert.That(disposalError).IsNotNull();
        await Assert.That(fixture.Store.Position).IsEqualTo(before);
        using var next = fixture.Capture(limits);
        byte[]? actual = null;
        var result = next.VisitPrefix(ReadCutFixture.Key(Entity), (_, value) =>
        {
            actual = value.ToArray();
            return false;
        });
        await Assert.That(result.Records).IsEqualTo(1);
        await Assert.That(actual).IsEquivalentTo(ReadCutFixture.Value(Value));
    }

    [Test]
    public async Task HandledDisposalFailureDoesNotLeaveAnUnobservedTask()
    {
        KeyLoadException? original = null;
        var unobserved = 0;
        void Observe(object? sender, UnobservedTaskExceptionEventArgs arguments)
        {
            if (arguments.Exception.InnerExceptions.Any(error => ReferenceEquals(error, original)))
            {
                Interlocked.Increment(ref unobserved);
            }
        }
        TaskScheduler.UnobservedTaskException += Observe;
        try
        {
            original = DisposeExpiredReadCut();
            await Assert.That(original.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Assert.That(Volatile.Read(ref unobserved)).IsEqualTo(0);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Observe;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static KeyLoadException DisposeExpiredReadCut()
    {
        var clock = new ReadCutAdvancingClock();
        using var fixture = new ReadCutFixture(clock);
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Put(ReadCutFixture.Key(Entity), ReadCutFixture.Value(Value));
            return true;
        });
        using var lease = fixture.Capture(ReadCutFixture.Limits(1, 1024, Lifetime));
        clock.Advance(Lifetime.Add(TimeSpan.FromTicks(1)));
        return Assert.ThrowsExactly<KeyLoadException>(lease.Dispose);
    }
}
