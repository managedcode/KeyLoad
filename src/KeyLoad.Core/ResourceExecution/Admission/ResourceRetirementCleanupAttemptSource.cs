using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireCleanupRetirementAttemptSource(IKeyValueView view, ResourceRetirementAttemptContextV1 context)
    {
        var state = RequireRetirementSource(view, context.Retirement!);
        ResourceRetirementWorkValidation.RequireBarrier(state, Limits);
        _ = ReadOriginalResourceRetirementWorkBarrier(view, state);
        var receiver = context.Receiver
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        if (state.Finalization is not null || state.OwnerCleanupReceipts.Any(cleanup =>
                PhysicalOwnerEntryValidation.Same(cleanup.Receiver, receiver)))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }

        if (!state.FinalInventory!.Entries.Any(entry => entry.Partition == context.Page!.Partition
            && PhysicalOwnerEntryValidation.SameOwner(entry.ObservedOwner, receiver.Owner)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
    }
}
