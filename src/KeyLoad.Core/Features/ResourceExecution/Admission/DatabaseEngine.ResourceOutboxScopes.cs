using KeyLoad.Core.Features.ChangeFeeds;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int ResourceOutboxPriorSequence = 1;

    private void ObservePurgedOutboxResources(IKeyValueView view, PurgeOutboxRequest request)
    {
        if (request.Limit < ResourceOutboxPriorSequence || request.Limit > Limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, MutationCountBudgetMessage);
        }
        var head = ReadOutboxHead(view, request.Partition);
        var through = Math.Min(head.Tail, request.ThroughSequence);
        foreach (var row in StoredOutboxReader.ReadRange(view, request.Partition,
            head.FirstAvailable - ResourceOutboxPriorSequence, through, request.Limit))
        {
            _ = Resource(view, request.Partition, row.Entry.Mutation.Resource);
        }
    }
}
