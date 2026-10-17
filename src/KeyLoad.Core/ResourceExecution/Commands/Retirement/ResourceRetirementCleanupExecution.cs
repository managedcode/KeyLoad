using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 ExecuteResourceRetirementCleanupPage(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementCleanupPageBodyV1>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        if (phase.RetirementId != body.SealedState.Control.Request.RetirementId)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }

        var checkpoint = ApplyResourceRetirementCleanupPage(transaction, principal, operation, body, position);
        return new(checkpoint.Receipt, null, null)
        {
            CleanupCheckpoint = checkpoint
        };
    }
}
