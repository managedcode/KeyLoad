using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeReadCutBudgetTests
{
    private const string FirstId = "one";
    private const string SecondId = "two";
    private const string FirstValue = "alpha";
    private const string SecondValue = "beta";

    [Test]
    public async Task AcCut002ExactRecordAndByteLimitsSucceedAndOneOverFailsBeforeCopy()
    {
        using var fixture = new NativeReadCutFixture();
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(NativeReadCutFixture.Key(FirstId), NativeReadCutFixture.Value(FirstValue));
            tx.Put(NativeReadCutFixture.Key(SecondId), NativeReadCutFixture.Value(SecondValue));
            return true;
        });
        var totalBytes = RowBytes(FirstId, FirstValue) + RowBytes(SecondId, SecondValue);
        using (var exact = fixture.Capture(NativeReadCutFixture.Limits(2, totalBytes)))
        {
            var delivered = 0;
            var result = exact.VisitPrefix(NativeReadCutFixture.Key(string.Empty), (_, _) => { delivered++; return true; });
            await Assert.That(result.Records).IsEqualTo(2);
            await Assert.That(result.ExaminedBytes).IsEqualTo(totalBytes);
            await Assert.That(delivered).IsEqualTo(2);
        }

        using (var recordBound = fixture.Capture(NativeReadCutFixture.Limits(1, totalBytes)))
        {
            var delivered = 0;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => recordBound.VisitPrefix(
                NativeReadCutFixture.Key(string.Empty), (_, _) => { delivered++; return true; }));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(delivered).IsEqualTo(1);
        }

        using (var byteBound = fixture.Capture(NativeReadCutFixture.Limits(2, totalBytes - 1)))
        {
            var delivered = 0;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => byteBound.VisitPrefix(
                NativeReadCutFixture.Key(string.Empty), (_, _) => { delivered++; return true; }));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(delivered).IsEqualTo(1);
        }
    }

    [Test]
    public async Task AcCut002InvalidLimitsAndForeignViewDoNotReserveTheNativeSlot()
    {
        using var fixture = new NativeReadCutFixture();
        var invalid = new[]
        {
            new ZoneTreeReadCutLimits(0, 1, TimeSpan.FromSeconds(1)),
            new ZoneTreeReadCutLimits(1, 0, TimeSpan.FromSeconds(1)),
            new ZoneTreeReadCutLimits(1, 1, TimeSpan.Zero),
            new ZoneTreeReadCutLimits(5_000_001, 1, TimeSpan.FromSeconds(1)),
            new ZoneTreeReadCutLimits(1, 1_073_741_825, TimeSpan.FromSeconds(1)),
            new ZoneTreeReadCutLimits(1, 1, TimeSpan.FromMinutes(1).Add(TimeSpan.FromTicks(1)))
        };
        foreach (var limits in invalid)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Capture(limits));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        }

        using var other = new NativeReadCutFixture();
        var wrongView = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view =>
            other.Store.CaptureNativeReadCut(view, NativeReadCutFixture.Limits(1, 1), default)));
        await Assert.That(wrongView.Code).IsEqualTo(ErrorCode.Validation);
        var outside = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.CaptureNativeReadCut(
            fixture.Store, NativeReadCutFixture.Limits(1, 1), default));
        await Assert.That(outside.Code).IsEqualTo(ErrorCode.Validation);
        using var healthy = fixture.Capture(NativeReadCutFixture.Limits(1, 1));
        await Assert.That(healthy.Cut.Position).IsEqualTo(fixture.Store.Position);
        var busy = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Capture(NativeReadCutFixture.Limits(1, 1)));
        await Assert.That(busy.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    [Test]
    public async Task AcCut002SequentialVisitsShareOneCumulativeRecordBudget()
    {
        using var fixture = new NativeReadCutFixture();
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(NativeReadCutFixture.Key(FirstId), NativeReadCutFixture.Value(FirstValue));
            tx.Put(NativeReadCutFixture.Key(SecondId), NativeReadCutFixture.Value(SecondValue));
            return true;
        });
        using (var lease = fixture.Capture(NativeReadCutFixture.Limits(1, 1024)))
        {
            var first = lease.VisitPrefix(NativeReadCutFixture.Key(string.Empty), static (_, _) => false);
            await Assert.That(first.Records).IsEqualTo(1);
            await Assert.That(first.StoppedByVisitor).IsTrue();
            var delivered = 0;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => lease.VisitPrefix(
                NativeReadCutFixture.Key(string.Empty), (_, _) => { delivered++; return false; }));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(delivered).IsEqualTo(0);
        }

        using var healthy = fixture.Capture(NativeReadCutFixture.Limits(2, 1024));
        await Assert.That(healthy.VisitPrefix(NativeReadCutFixture.Key(string.Empty), static (_, _) => true).Records)
            .IsEqualTo(2);
    }

    private static long RowBytes(string key, string value)
        => NativeReadCutFixture.Key(key).Length + NativeReadCutFixture.Value(value).Length + 1L;
}
