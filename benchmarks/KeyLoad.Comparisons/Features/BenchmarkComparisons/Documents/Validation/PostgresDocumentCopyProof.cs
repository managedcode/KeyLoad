using System.Collections.Immutable;
using System.Text;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresDocumentCopyProof
{
    private const int SingleCopyCount = 1, ReplicatedCopyCount = 3;
    private const string Failure = "PostgresFinalDocumentCopyReceiptMissing";
    private const string WalReceiptPrefix = "Named standbys flushed and replayed seeded corpus through WAL ";
    private const string InitialCorpusLabel = "seeded corpus", FinalCorpusLabel = "final document state";
    private const string FinalStateObservation = "Final document state records={0}; sha256={1}; fresh native WAL flush/replay observation after full primary readback";
    private const string MembershipPrefix = "Native standby identities=";
    private static readonly CompositeFormat FinalStateObservationFormat = CompositeFormat.Parse(FinalStateObservation);
    internal static TargetProfile BindFinalState(TargetProfile observed, TargetProfile initial, DocumentComparisonSchedule schedule)
    {
        var current = observed.Cluster ?? throw new ComparisonFailureException(Failure);
        var previous = initial.Cluster ?? throw new ComparisonFailureException(Failure);
        if (current.Nodes is not (SingleCopyCount or ReplicatedCopyCount) || current.Nodes != previous.Nodes || current.DataCopies != current.Nodes
            || (current.Nodes == ReplicatedCopyCount && (!current.Observations.Any(value => value.StartsWith(WalReceiptPrefix, StringComparison.Ordinal))
                || !current.Observations.Where(value => value.StartsWith(MembershipPrefix, StringComparison.Ordinal))
                    .SequenceEqual(previous.Observations.Where(value => value.StartsWith(MembershipPrefix, StringComparison.Ordinal)), StringComparer.Ordinal))))
        {
            throw new ComparisonFailureException(Failure);
        }

        var observations = current.Observations.Select(value => value.Replace(InitialCorpusLabel, FinalCorpusLabel, StringComparison.Ordinal))
            .Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, FinalStateObservationFormat, schedule.Final, DocumentComparisonOracle.Digest(schedule.FinalDocuments()))).ToImmutableArray();
        return observed with { Cluster = current with { Observations = observations } };
    }
}
