using KeyLoad.Core.Features.ChangeFeeds;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ResourceExecutionAdmissionResourceOutboxScopes operations against the caller's database context.</summary>
public static class ResourceExecutionAdmissionResourceOutboxScopes
{
    internal const int ResourceOutboxPriorSequence = 1;
    internal static void ObservePurgedOutboxResources(this DatabaseEngine engine, IKeyValueView view, PurgeOutboxRequest request)
    {
        if (request.Limit < ResourceOutboxPriorSequence || request.Limit > engine.Limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AuthorizationCommandsCommandAuthorization.MutationCountBudgetMessage);
        }

        var head = engine.ReadOutboxHead(view, request.Partition);
        var through = Math.Min(head.Tail, request.ThroughSequence);
        foreach (var row in StoredOutboxReader.ReadRange(view, request.Partition, head.FirstAvailable - ResourceOutboxPriorSequence, through, request.Limit))
        {
            _ = engine.Resource(view, request.Partition, row.Entry.Mutation.Resource);
        }
    }
}
