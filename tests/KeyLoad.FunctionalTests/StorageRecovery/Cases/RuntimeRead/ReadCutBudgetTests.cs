using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class ReadCutBudgetTests
{
    private const string FirstId = "one";
    private const string SecondId = "two";
    private const string FirstValue = "alpha";
    private const string SecondValue = "beta";

    [Test]
    public async Task AcCut002ExactRecordAndByteLimitsSucceedAndOneOverFailsBeforeCopy()
    {
        using var fixture = new ReadCutFixture();
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(ReadCutFixture.Key(FirstId), ReadCutFixture.Value(FirstValue));
            tx.Put(ReadCutFixture.Key(SecondId), ReadCutFixture.Value(SecondValue));
            return true;
        });
        var totalBytes = RowBytes(FirstId, FirstValue) + RowBytes(SecondId, SecondValue);
        using (var exact = fixture.Capture(ReadCutFixture.Limits(2, totalBytes)))
        {
            var delivered = 0;
            var result = exact.VisitPrefix(ReadCutFixture.Key(string.Empty), (_, _) => { delivered++; return true; });
            await Assert.That(result.Records).IsEqualTo(2);
            await Assert.That(result.ExaminedBytes).IsEqualTo(totalBytes);
            await Assert.That(delivered).IsEqualTo(2);
        }

        using (var recordBound = fixture.Capture(ReadCutFixture.Limits(1, totalBytes)))
        {
            var delivered = 0;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => recordBound.VisitPrefix(
                ReadCutFixture.Key(string.Empty), (_, _) => { delivered++; return true; }));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(delivered).IsEqualTo(1);
        }

        using (var byteBound = fixture.Capture(ReadCutFixture.Limits(2, totalBytes - 1)))
        {
            var delivered = 0;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => byteBound.VisitPrefix(
                ReadCutFixture.Key(string.Empty), (_, _) => { delivered++; return true; }));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(delivered).IsEqualTo(1);
        }
    }

    [Test]
    public async Task AcCut002InvalidLimitsAndForeignViewDoNotReserveTheSlot()
    {
        using var fixture = new ReadCutFixture();
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

        using var other = new ReadCutFixture();
        var wrongView = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view =>
            other.Store.CaptureReadCut(view, ReadCutFixture.Limits(1, 1), default)));
        await Assert.That(wrongView.Code).IsEqualTo(ErrorCode.Validation);
        var outside = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.CaptureReadCut(
            fixture.Store, ReadCutFixture.Limits(1, 1), default));
        await Assert.That(outside.Code).IsEqualTo(ErrorCode.Validation);
        using var healthy = fixture.Capture(ReadCutFixture.Limits(1, 1));
        await Assert.That(healthy.Cut.Position).IsEqualTo(fixture.Store.Position);
        var busy = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Capture(ReadCutFixture.Limits(1, 1)));
        await Assert.That(busy.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    [Test]
    public async Task AcCut002SequentialVisitsShareOneCumulativeRecordBudget()
    {
        using var fixture = new ReadCutFixture();
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(ReadCutFixture.Key(FirstId), ReadCutFixture.Value(FirstValue));
            tx.Put(ReadCutFixture.Key(SecondId), ReadCutFixture.Value(SecondValue));
            return true;
        });
        using (var lease = fixture.Capture(ReadCutFixture.Limits(1, 1024)))
        {
            var first = lease.VisitPrefix(ReadCutFixture.Key(string.Empty), static (_, _) => false);
            await Assert.That(first.Records).IsEqualTo(1);
            await Assert.That(first.StoppedByVisitor).IsTrue();
            var delivered = 0;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => lease.VisitPrefix(
                ReadCutFixture.Key(string.Empty), (_, _) => { delivered++; return false; }));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(delivered).IsEqualTo(0);
        }

        using var healthy = fixture.Capture(ReadCutFixture.Limits(2, 1024));
        await Assert.That(healthy.VisitPrefix(ReadCutFixture.Key(string.Empty), static (_, _) => true).Records)
            .IsEqualTo(2);
    }

    private static long RowBytes(string key, string value)
        => ReadCutFixture.Key(key).Length + ReadCutFixture.Value(value).Length;
}
