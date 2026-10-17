using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string WorkFenceAlreadyAttached = "The native resource work fence is already attached.";
    private const string WorkFenceNotAttached = "The native resource work fence is not attached.";
    private const string InvalidWorkFenceClosure = "The native resource work fence returned no retained settlement.";
    private IResourceLifetimeWorkFence? resourceLifetimeWorkFence;

    internal void AttachResourceLifetimeWorkFence(IResourceLifetimeWorkFence owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (Interlocked.CompareExchange(ref resourceLifetimeWorkFence, owner, null) is not null)
        {
            throw Errors.Fail(ErrorCode.Validation, WorkFenceAlreadyAttached);
        }
    }

    internal void RequireResourceLifetimeWorkFenceAttached()
    {
        if (Volatile.Read(ref resourceLifetimeWorkFence) is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, WorkFenceNotAttached);
        }
    }

    private void ClosePublishedResourceRetirementFence(OperationResult result)
    {
        if (result.Error is not null
            || result.NativeValue is not ResourceRetirementPhaseResultV1 { Fence: not null } phase
            || Volatile.Read(ref resourceLifetimeWorkFence) is null)
        {
            return;
        }
        Store.Read(view =>
        {
            var actual = ResourceRetirementStorage.ReadFence(view, phase.Fence.Control.Lifetime, Limits)
                ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            if (!ResourceRetirementValidation.Same(actual, phase.Fence))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            CloseCommittedResourceRetirementFence(view, actual);
            return true;
        });
    }

    private void CloseCommittedResourceRetirementFence(IKeyValueView view, ResourceRetirementOwnerFenceV1 actual)
    {
        var acknowledged = ResourceRetirementWorkStorage.ReadOwner(view, actual.Control, Limits);
        if (acknowledged is not null)
        {
            if (!ResourceRetirementValidation.Same(acknowledged.Fence, actual))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            return;
        }
        var owner = Volatile.Read(ref resourceLifetimeWorkFence);
        if (owner is not null)
        {
            // The existing native owner retains this exact original handle and terminal errors.
            // Cancellation and joining belong to the separate D4 call after all storage/apply gates.
            _ = owner.CloseMatching(actual)
                ?? throw Errors.Fail(ErrorCode.Corruption, InvalidWorkFenceClosure);
        }
    }
}
