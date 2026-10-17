using System.Globalization;
using System.Text;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-006L: bounded retention preserves original bytes and chronological capture order.</summary>
internal sealed class ComparisonReplayDiagnosticRetentionTests
{
    /// <summary>Ten thousand ordinary errors cannot displace configuration and the latest original quota.</summary>
    [Test]
    public async Task OrdinaryFloodRetainsLatestOriginalDiagnosticsAndTail()
    {
        var buffer = new ComparisonResourceLogBuffer(2000, 1_048_576, 16_384);
        var config = ComparisonReplayDiagnosticLogData.Config(3);
        var first = ComparisonReplayDiagnosticLogData.Quota(0, 2, 3);
        var latest = ComparisonReplayDiagnosticLogData.Quota(0, 2, 3, suppressed: 7);
        buffer.Add(config);
        buffer.Add(first);
        for (var index = 0; index < 10_000; index++)
        {
            buffer.Add(ComparisonReplayDiagnosticLogData.Ordinary + index.ToString(CultureInfo.InvariantCulture));
        }
        buffer.Add(latest);
        var snapshot = buffer.Snapshot();
        await Assert.That(snapshot.Length).IsEqualTo(2000);
        await Assert.That(snapshot[0]).IsEqualTo(config);
        await Assert.That(snapshot[^1]).IsEqualTo(latest);
        await Assert.That(snapshot).DoesNotContain(first);
        await Assert.That(snapshot[1]).IsEqualTo(ComparisonReplayDiagnosticLogData.Ordinary + "8002");
        await Assert.That(snapshot.Sum(line => Encoding.UTF8.GetByteCount(line) + 1)).IsLessThanOrEqualTo(1_048_576);
    }

    /// <summary>Exactly thirteen slots retain independent latest keys in capture order, not timestamp order.</summary>
    [Test]
    public async Task FixedSlotsReplaceLatestKeysWithoutReorderingOtherOriginalLines()
    {
        var buffer = new ComparisonResourceLogBuffer(20, 16_384, 1024);
        var expected = new List<string> { ComparisonReplayDiagnosticLogData.Config(3) };
        buffer.Add(expected[0]);
        for (var sender = 0; sender < 3; sender++)
        {
            for (var pool = 0; pool < 4; pool++)
            {
                var line = ComparisonReplayDiagnosticLogData.Quota(sender, pool, 3);
                buffer.Add(line);
                expected.Add(line);
            }
        }
        var replaced = ComparisonReplayDiagnosticLogData.Quota(1, 1, 3);
        expected.Remove(replaced);
        var latest = ComparisonReplayDiagnosticLogData.Quota(1, 1, 3, suppressed: long.MaxValue)
            .Replace(ComparisonReplayDiagnosticLogData.Timestamp, ComparisonReplayDiagnosticLogData.EarlierTimestamp, StringComparison.Ordinal);
        buffer.Add(latest);
        expected.Add(latest);
        await Assert.That(buffer.Snapshot().SequenceEqual(expected)).IsTrue();
        await Assert.That(buffer.Snapshot().Sum(line => Encoding.UTF8.GetByteCount(line) + 1)).IsLessThanOrEqualTo(8192);
    }

    /// <summary>Independent consistent quotas can precede config, while incompatible ones lose protection in place.</summary>
    /// <param name="replace">Whether an already retained configuration is replaced.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfigurationTransitionDemotesMismatchingQuotaWithoutRewritingIt(bool replace)
    {
        var buffer = new ComparisonResourceLogBuffer(3, 4096, 1024);
        var oldQuota = ComparisonReplayDiagnosticLogData.Quota(0, 2, 1);
        var currentQuota = ComparisonReplayDiagnosticLogData.Quota(0, 2, 3);
        var config = ComparisonReplayDiagnosticLogData.Config(3);
        if (replace)
        {
            buffer.Add(ComparisonReplayDiagnosticLogData.Config(1));
        }
        buffer.Add(oldQuota);
        buffer.Add(config);
        await Assert.That(buffer.Snapshot().SequenceEqual(new[] { oldQuota, config })).IsTrue();
        buffer.Add(currentQuota);
        buffer.Add(ComparisonReplayDiagnosticLogData.Ordinary);
        await Assert.That(buffer.Snapshot().SequenceEqual(new[] { config, currentQuota, ComparisonReplayDiagnosticLogData.Ordinary })).IsTrue();
        buffer.Add(oldQuota);
        buffer.Add(ComparisonReplayDiagnosticLogData.Ordinary + ComparisonReplayDiagnosticLogData.Ordinary);
        await Assert.That(buffer.Snapshot()).DoesNotContain(oldQuota);
        await Assert.That(buffer.Snapshot()).Contains(currentQuota);
    }

    /// <summary>Independently consistent quota records cannot replace evidence when their retained configuration disagrees.</summary>
    /// <param name="mismatch">Selected capacity, another pool count or node maximum disagreement.</param>
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task ConfigurationMismatchRemainsOrdinaryWithoutDisplacingLatestQuota(int mismatch)
    {
        var original = ComparisonReplayDiagnosticLogData.Quota(0, 2, 3);
        var changed = mismatch switch
        {
            0 => original.Replace(ComparisonReplayDiagnosticLogData.Read, ComparisonReplayDiagnosticLogData.WrongRead, StringComparison.Ordinal)
                .Replace(ComparisonReplayDiagnosticLogData.Capacity, ComparisonReplayDiagnosticLogData.WrongCapacity, StringComparison.Ordinal),
            1 => original.Replace(ComparisonReplayDiagnosticLogData.Critical, ComparisonReplayDiagnosticLogData.OtherPoolMismatch, StringComparison.Ordinal),
            _ => original.Replace(ComparisonReplayDiagnosticLogData.NodeMaximum, ComparisonReplayDiagnosticLogData.OtherNodeMaximum, StringComparison.Ordinal)
                .Replace(ComparisonReplayDiagnosticLogData.VoterMaximum, ComparisonReplayDiagnosticLogData.OtherVoterMaximum, StringComparison.Ordinal)
        };
        await Assert.That(ComparisonReplayDiagnosticLog.TryRead(changed, out _)).IsTrue();
        var config = ComparisonReplayDiagnosticLogData.Config(3);
        var buffer = new ComparisonResourceLogBuffer(3, 4096, 1024);
        buffer.Add(config);
        buffer.Add(original);
        buffer.Add(changed);
        buffer.Add(ComparisonReplayDiagnosticLogData.Ordinary);
        await Assert.That(buffer.Snapshot().SequenceEqual(new[] { config, original, ComparisonReplayDiagnosticLogData.Ordinary })).IsTrue();
    }

    /// <summary>Total bytes/lines outrank protection; ordinary lines are evicted before the oldest protected record.</summary>
    [Test]
    public async Task TinyBudgetsEvictOrdinaryBeforeProtectedAndPreserveUtf8Boundaries()
    {
        var first = ComparisonReplayDiagnosticLogData.Quota(0, 2, 3);
        var latest = ComparisonReplayDiagnosticLogData.Quota(1, 2, 3);
        var buffer = new ComparisonResourceLogBuffer(2, Encoding.UTF8.GetByteCount(latest) + 1, 1024);
        buffer.Add(first);
        buffer.Add(ComparisonReplayDiagnosticLogData.Ordinary);
        await Assert.That(buffer.Snapshot().SequenceEqual(new[] { first })).IsTrue();
        buffer.Add(latest);
        await Assert.That(buffer.Snapshot().SequenceEqual(new[] { latest })).IsTrue();
        var tooSmall = new ComparisonResourceLogBuffer(2, first.Length, 1024);
        tooSmall.Add(first);
        await Assert.That(tooSmall.Snapshot()).IsEmpty();
        var unicode = new ComparisonResourceLogBuffer(3, 20, 8);
        unicode.Add(ComparisonReplayDiagnosticLogData.Unicode);
        await Assert.That(unicode.Snapshot().SequenceEqual(new[] { ComparisonReplayDiagnosticLogData.BoundedUnicode })).IsTrue();
    }

    /// <summary>Truncating a valid-looking prefix followed by secret text never creates protected evidence.</summary>
    [Test]
    public async Task TruncatedOriginalNeverBecomesProtectedDiagnostic()
    {
        var valid = ComparisonReplayDiagnosticLogData.Quota(0, 2, 3);
        var buffer = new ComparisonResourceLogBuffer(1, 4096, valid.Length);
        buffer.Add(valid + ComparisonReplayDiagnosticLogData.Secret);
        await Assert.That(buffer.Snapshot().SequenceEqual(new[] { valid })).IsTrue();
        buffer.Add(ComparisonReplayDiagnosticLogData.Ordinary);
        await Assert.That(buffer.Snapshot().SequenceEqual(new[] { ComparisonReplayDiagnosticLogData.Ordinary })).IsTrue();
    }
}
