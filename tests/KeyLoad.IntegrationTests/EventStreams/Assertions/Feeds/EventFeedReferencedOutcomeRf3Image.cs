using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed record EventFeedReferencedOutcomeRf3Image(long Position, string[] Rows)
{
    private const long HexStringStorageMultiplier = 4;
    private const string Exhausted = "The complete referenced-outcome fixture image exceeds its native bound.";

    internal static EventFeedReferencedOutcomeRf3Image Capture(ZoneTreeStore store, CancellationToken token)
        => store.Read(view =>
        {
            var options = IntegrationExecutionOptions.DatabaseLimits();
            var budget = new EventVectorInventoryReadBudget(options);
            var rows = new List<string>();
            var scan = view.VisitRange([], options.Value.MaxScanRecords, (key, value) =>
            {
                rows.Add(Convert.ToHexString(key) + ":" + Convert.ToHexString(value));
                return true;
            }, observer: bytes => budget.Observe(checked(bytes * HexStringStorageMultiplier + sizeof(char))),
                cancellationToken: token);
            if (scan.HasMore)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, Exhausted);
            }
            return new EventFeedReferencedOutcomeRf3Image(store.Position, rows.ToArray());
        });
}
