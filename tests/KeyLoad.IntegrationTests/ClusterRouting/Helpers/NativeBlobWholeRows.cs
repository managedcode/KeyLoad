using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWholeRows
{
    internal const int MaximumRecords = 10_000;
    internal static (long Position, string[] Rows) Read(ZoneTreeStore store, int maximumRecords,
        C1SubmissionJournalInspectionReadBudget budget, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumRecords, MaximumRecords);
        return store.Read(view =>
        {
            var rows = new List<string>();
            var scan = view.VisitRange([], maximumRecords, (key, value) =>
            {
                token.ThrowIfCancellationRequested();
                rows.Add(Convert.ToHexString(key) + ":" + Convert.ToHexString(value));
                return true;
            }, observer: budget.Charge, cancellationToken: token);
            if (scan.HasMore || scan.StoppedByVisitor)
            {
                throw new InvalidOperationException("The complete original Blob native cut exceeds its record budget.");
            }
            return (store.Position, rows.ToArray());
        });
    }

    internal static byte[] Required(string[] rows, byte[] key, C1SubmissionJournalInspectionReadBudget budget)
    {
        var prefix = Convert.ToHexString(key) + ":";
        var row = rows.SingleOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal))
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, "The complete original Blob journal record is not retained.");
        budget.Charge(checked(key.Length + (row.Length - prefix.Length) / 2));
        return Convert.FromHexString(row.AsSpan(prefix.Length));
    }
}
