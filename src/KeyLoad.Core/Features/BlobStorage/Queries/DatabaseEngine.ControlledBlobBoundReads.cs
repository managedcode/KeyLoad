using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ControlledBlobReadFrameV2? TryCaptureControlledBlobReadV2Bound(string principalId,
        ControlledBlobReadPurpose purpose, ReadOnlyMemory<byte> nativeRequest, DateTimeOffset expiry,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return TryCaptureControlledBlobReadV2(principalId, purpose, nativeRequest, expiry, budget);
    }

    internal ControlledBlobReadFrameV2? TryCaptureControlledBlobOutcomeV2Bound(string principalId,
        ReplicatedOperation operation, DateTimeOffset expiry, ReadExecutionBudget budget,
        IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return TryCaptureControlledBlobOutcomeV2(principalId, operation, expiry, budget);
    }

    internal object? ReadControlledBlobV2Bound(string localPrincipalId, ControlledBlobReadFrameV2 frame,
        ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return ReadControlledBlobV2(localPrincipalId, frame, budget, grant, token);
    }

    internal object? ReadControlledBlobOutcomeV2Bound(string localPrincipalId, ControlledBlobReadFrameV2 frame,
        ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return ReadControlledBlobOutcomeV2(localPrincipalId, frame, budget, grant, token);
    }
}
