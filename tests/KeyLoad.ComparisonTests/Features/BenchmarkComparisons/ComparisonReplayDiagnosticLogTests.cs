using System.Globalization;
using System.Text;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-006L: closed original numeric lines are algorithm inputs, not native execution evidence.</summary>
internal sealed class ComparisonReplayDiagnosticLogTests
{
    /// <summary>Exact original native prefix and consistent one/two/three-voter arithmetic define closed protected keys.</summary>
    /// <param name="voters">The actual configuration cardinality represented by the numeric input.</param>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ConsistentConfigurationAndQuotaHaveClosedKeys(int voters)
    {
        await Assert.That(ComparisonReplayDiagnosticLog.TryRead(ComparisonReplayDiagnosticLogData.Config(voters), out var config)).IsTrue();
        await Assert.That(config.Key).IsEqualTo(0);
        await Assert.That(config.Voters).IsEqualTo(voters);
        for (var sender = 0; sender < voters; sender++)
        {
            for (var pool = 0; pool < 4; pool++)
            {
                var original = ComparisonReplayDiagnosticLogData.Quota(sender, pool, voters);
                await Assert.That(ComparisonReplayDiagnosticLog.TryRead(original, out var quota)).IsTrue();
                await Assert.That(quota.Key).IsEqualTo(1 + sender * 4 + pool);
                await Assert.That(quota.Matches(config)).IsTrue();
            }
        }
    }

    /// <summary>Malformed/overflow/secret-bearing input cannot replace an existing protected quota.</summary>
    /// <param name="field">The exact original grammar token to replace.</param>
    /// <param name="replacement">A concrete malformed or inconsistent replacement.</param>
    [Test]
    [Arguments(ComparisonReplayDiagnosticLogData.Timestamp, ComparisonReplayDiagnosticLogData.InvalidDate)]
    [Arguments(ComparisonReplayDiagnosticLogData.Timestamp, ComparisonReplayDiagnosticLogData.InvalidPrecision)]
    [Arguments(ComparisonReplayDiagnosticLogData.Indent, ComparisonReplayDiagnosticLogData.ShortIndent)]
    [Arguments(ComparisonReplayDiagnosticLogData.Sender, ComparisonReplayDiagnosticLogData.BadSender)]
    [Arguments(ComparisonReplayDiagnosticLogData.Pool, ComparisonReplayDiagnosticLogData.BadPool)]
    [Arguments(ComparisonReplayDiagnosticLogData.Method, ComparisonReplayDiagnosticLogData.BadMethod)]
    [Arguments(ComparisonReplayDiagnosticLogData.Read, ComparisonReplayDiagnosticLogData.NegativeRead)]
    [Arguments(ComparisonReplayDiagnosticLogData.Read, ComparisonReplayDiagnosticLogData.WrongRead)]
    [Arguments(ComparisonReplayDiagnosticLogData.Critical, ComparisonReplayDiagnosticLogData.ExcessiveCritical)]
    [Arguments(ComparisonReplayDiagnosticLogData.Capacity, ComparisonReplayDiagnosticLogData.ZeroCapacity)]
    [Arguments(ComparisonReplayDiagnosticLogData.Capacity, ComparisonReplayDiagnosticLogData.WrongCapacity)]
    [Arguments(ComparisonReplayDiagnosticLogData.VoterMaximum, ComparisonReplayDiagnosticLogData.ZeroMaximum)]
    [Arguments(ComparisonReplayDiagnosticLogData.NodeMaximum, ComparisonReplayDiagnosticLogData.WrongMaximum)]
    [Arguments(ComparisonReplayDiagnosticLogData.NodeMaximum, ComparisonReplayDiagnosticLogData.TooManyVoters)]
    [Arguments(ComparisonReplayDiagnosticLogData.NodeMaximum, ComparisonReplayDiagnosticLogData.TooFewVoters)]
    [Arguments(ComparisonReplayDiagnosticLogData.Observed, ComparisonReplayDiagnosticLogData.NegativeObserved)]
    [Arguments(ComparisonReplayDiagnosticLogData.Oldest, ComparisonReplayDiagnosticLogData.OverflowOldest)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.NegativeSuppressed)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.OverflowSuppressed)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.DuplicateSuppressed)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.SecretSuffix)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.UnknownSuffix)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.LeadingZero)]
    public async Task MalformedQuotaCannotDisplaceProtectedEvidence(string field, string replacement)
    {
        var original = ComparisonReplayDiagnosticLogData.Quota(0, 2, 3);
        var malformed = original.Replace(field, replacement, StringComparison.Ordinal);
        await Assert.That(ComparisonReplayDiagnosticLog.TryRead(malformed, out _)).IsFalse();
        var buffer = new ComparisonResourceLogBuffer(2, 2048, 1024);
        buffer.Add(original);
        buffer.Add(malformed);
        buffer.Add(ComparisonReplayDiagnosticLogData.Ordinary);
        await Assert.That(buffer.Snapshot().SequenceEqual(new[] { original, ComparisonReplayDiagnosticLogData.Ordinary })).IsTrue();
    }

    /// <summary>Unknown prefix, embedded message and invalid configuration arithmetic remain ordinary.</summary>
    /// <param name="invalid">The complete invalid original line.</param>
    [Test]
    [Arguments(ComparisonReplayDiagnosticLogData.SecretPrefix)]
    [Arguments(ComparisonReplayDiagnosticLogData.BareMessage)]
    [Arguments(ComparisonReplayDiagnosticLogData.ZeroVoters)]
    [Arguments(ComparisonReplayDiagnosticLogData.ExcessVoters)]
    [Arguments(ComparisonReplayDiagnosticLogData.ZeroConfig)]
    [Arguments(ComparisonReplayDiagnosticLogData.WrongConfig)]
    [Arguments(ComparisonReplayDiagnosticLogData.OversizedConfig)]
    [Arguments(ComparisonReplayDiagnosticLogData.OverflowConfig)]
    public async Task InvalidOriginalConfigurationOrPrefixIsNotProtected(string invalid)
        => await Assert.That(ComparisonReplayDiagnosticLog.TryRead(invalid, out _)).IsFalse();

    /// <summary>Actual control characters and non-ASCII digits cannot claim protection through BCL numeric compatibility.</summary>
    /// <param name="configuration">Whether configuration or quota numeric bytes are corrupted.</param>
    /// <param name="corruption">The original token character corruption.</param>
    [Test]
    [Arguments(false, ComparisonReplayDiagnosticNumericToken.TrailingNull)]
    [Arguments(false, ComparisonReplayDiagnosticNumericToken.EmbeddedNull)]
    [Arguments(false, ComparisonReplayDiagnosticNumericToken.Tab)]
    [Arguments(false, ComparisonReplayDiagnosticNumericToken.NonAscii)]
    [Arguments(true, ComparisonReplayDiagnosticNumericToken.TrailingNull)]
    [Arguments(true, ComparisonReplayDiagnosticNumericToken.EmbeddedNull)]
    [Arguments(true, ComparisonReplayDiagnosticNumericToken.Tab)]
    [Arguments(true, ComparisonReplayDiagnosticNumericToken.NonAscii)]
    public async Task NonAsciiNumericTokenCannotDisplaceOriginalEvidence(bool configuration, ComparisonReplayDiagnosticNumericToken corruption)
    {
        var config = ComparisonReplayDiagnosticLogData.Config(3);
        var quota = ComparisonReplayDiagnosticLogData.Quota(0, 2, 3, suppressed: 17);
        var token = configuration ? ComparisonReplayDiagnosticLogData.NodeValue : ComparisonReplayDiagnosticLogData.SuppressedValue;
        var changed = corruption switch
        {
            ComparisonReplayDiagnosticNumericToken.TrailingNull => token + ComparisonReplayDiagnosticLogData.Null,
            ComparisonReplayDiagnosticNumericToken.EmbeddedNull => token.Insert(1, ComparisonReplayDiagnosticLogData.Null),
            ComparisonReplayDiagnosticNumericToken.Tab => token + ComparisonReplayDiagnosticLogData.Tab,
            _ => configuration ? ComparisonReplayDiagnosticLogData.NonAsciiNode : ComparisonReplayDiagnosticLogData.NonAsciiSuppressed
        };
        var field = configuration ? ComparisonReplayDiagnosticLogData.NodeField : ComparisonReplayDiagnosticLogData.SuppressedField;
        var malformed = (configuration ? config : quota).Replace(field + token, field + changed, StringComparison.Ordinal);
        await Assert.That(ComparisonReplayDiagnosticLog.TryRead(malformed, out _)).IsFalse();
        var buffer = new ComparisonResourceLogBuffer(3, 4096, 1024);
        buffer.Add(config);
        buffer.Add(quota);
        buffer.Add(malformed);
        buffer.Add(ComparisonReplayDiagnosticLogData.Ordinary);
        await Assert.That(buffer.Snapshot().SequenceEqual(new[] { config, quota, ComparisonReplayDiagnosticLogData.Ordinary })).IsTrue();
    }
}

internal enum ComparisonReplayDiagnosticNumericToken { TrailingNull, EmbeddedNull, Tab, NonAscii }

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
        { buffer.Add(ComparisonReplayDiagnosticLogData.Config(1)); }
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

internal static class ComparisonReplayDiagnosticLogData
{
    internal const string Timestamp = "2026-10-03T02:16:40.3446969Z";
    internal const string EarlierTimestamp = "2026-10-02T02:16:40.3446969Z";
    internal const string InvalidDate = "2026-02-30T02:16:40.3446969Z";
    internal const string InvalidPrecision = "2026-10-03T02:16:40.344696Z";
    internal const string Indent = "       ", ShortIndent = "      ";
    internal const string Ordinary = "ordinary-native-error-";
    internal const string Secret = " credential-marker";
    internal const string Unicode = "🙂🙂🙂🙂🙂", BoundedUnicode = "🙂🙂";
    private const string Prefix = Timestamp + Indent;
    private const string Configured = "ReplicaReplayConfigured voters=";
    private const string Capacities = " critical=2 forward=3 read=4 data=5 nodeMaximum=";
    internal const string Sender = "senderIndex=0", BadSender = "senderIndex=3";
    internal const string Pool = "pool=2", BadPool = "pool=4";
    internal const string Method = "method=7", BadMethod = "method=9";
    internal const string Read = "read=4", NegativeRead = "read=-1", WrongRead = "read=3";
    internal const string Critical = "critical=0", ExcessiveCritical = "critical=14";
    internal const string Capacity = "capacity=4", ZeroCapacity = "capacity=0", WrongCapacity = "capacity=3";
    internal const string VoterMaximum = "voterMaximum=14", ZeroMaximum = "voterMaximum=0";
    internal const string NodeMaximum = "nodeMaximum=42", WrongMaximum = "nodeMaximum=43", TooManyVoters = "nodeMaximum=56", TooFewVoters = "nodeMaximum=7";
    internal const string Observed = "observedUnixMs=1790990000000", NegativeObserved = "observedUnixMs=-1";
    internal const string Oldest = "oldestExpiryUnixMs=1790990030000", OverflowOldest = "oldestExpiryUnixMs=9223372036854775808";
    internal const string Suppressed = "suppressed=0", NegativeSuppressed = "suppressed=-1", OverflowSuppressed = "suppressed=9223372036854775808";
    internal const string DuplicateSuppressed = Suppressed + " suppressed=0", SecretSuffix = Suppressed + Secret;
    internal const string UnknownSuffix = Suppressed + " unknown=0", LeadingZero = "suppressed=00";
    internal const string BareMessage = "ReplicaReplayConfigured voters=3 critical=2 forward=3 read=4 data=5 nodeMaximum=42";
    internal const string SecretPrefix = "credential-marker" + Prefix + BareMessage;
    internal const string ZeroConfig = Prefix + "ReplicaReplayConfigured voters=3 critical=0 forward=3 read=4 data=5 nodeMaximum=36";
    internal const string ZeroVoters = Prefix + "ReplicaReplayConfigured voters=0 critical=2 forward=3 read=4 data=5 nodeMaximum=0";
    internal const string ExcessVoters = Prefix + "ReplicaReplayConfigured voters=4 critical=2 forward=3 read=4 data=5 nodeMaximum=56";
    internal const string WrongConfig = Prefix + "ReplicaReplayConfigured voters=3 critical=2 forward=3 read=4 data=5 nodeMaximum=43";
    internal const string OversizedConfig = Prefix + "ReplicaReplayConfigured voters=3 critical=1048576 forward=3 read=4 data=5 nodeMaximum=3145764";
    internal const string OverflowConfig = Prefix + "ReplicaReplayConfigured voters=3 critical=9223372036854775807 forward=3 read=4 data=5 nodeMaximum=42";
    internal const string OtherPoolMismatch = "critical=3", OtherNodeMaximum = "nodeMaximum=45", OtherVoterMaximum = "voterMaximum=15";
    internal const string NodeField = "nodeMaximum=", SuppressedField = "suppressed=";
    internal const string NodeValue = "42", SuppressedValue = "17";
    internal const string Null = "\0", Tab = "\t", NonAsciiNode = "\u0664\u0662", NonAsciiSuppressed = "\u0661\u0667";
    private const string QuotaFormat = Prefix + "ReplicaReplayCapacity senderIndex={0} pool={1} method={2} "
        + "critical={3} forward={4} read={5} data={6} capacity={7} voterMaximum=14 nodeMaximum={8} "
        + "observedUnixMs=1790990000000 oldestExpiryUnixMs=1790990030000 suppressed={9}";
    private static readonly CompositeFormat Format = CompositeFormat.Parse(QuotaFormat);

    internal static string Config(int voters) => Prefix + Configured + voters.ToString(CultureInfo.InvariantCulture)
        + Capacities + (14 * voters).ToString(CultureInfo.InvariantCulture);

    internal static string Quota(int sender, int pool, int voters, long suppressed = 0)
    {
        long[] counts = [0, 0, 0, 0];
        counts[pool] = pool + 2;
        int[] methods = [0, 2, 7, 1];
        return string.Format(CultureInfo.InvariantCulture, Format, sender, pool, methods[pool], counts[0], counts[1],
            counts[2], counts[3], counts[pool], 14 * voters, suppressed);
    }
}
