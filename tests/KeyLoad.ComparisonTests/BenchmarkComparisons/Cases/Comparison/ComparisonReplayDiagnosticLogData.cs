using System.Globalization;
using System.Text;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

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
