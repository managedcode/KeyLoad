using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireCleanupOwnerWork(IKeyValueView view, ResourceRetirementCleanupPageBodyV1 body,
        ResourceRetirementOwnerFenceV1 fence)
    {
        ResourceRetirementWorkValidation.RequireBarrier(body.SealedState, Limits);
        var expected = body.SealedState.WorkBarrierReceipt!.Owners.SingleOrDefault(owner =>
            PhysicalOwnerEntryValidation.Same(owner.Receiver, body.Receiver))
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        var actual = ResourceRetirementWorkStorage.ReadOwner(view, body.SealedState.Control, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        if (!ResourceRetirementValidation.Same(actual, expected)
            || !ResourceRetirementValidation.Same(actual.Fence, fence))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
    }
}
