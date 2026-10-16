using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal object? ReadControlledBlobOutcomeV2(string localPrincipalId, ControlledBlobReadFrameV2 frame,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant, CancellationToken cancellationToken)
    {
        work.Check();
        if (frame is null || frame.Original is null || frame.Original.Purpose != ControlledBlobReadPurpose.Outcome)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, PartitionMoveProtocol.Invalid);
        }
        if (NativeSerialization.Measure(frame) > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
        }
        work.MeasureResult(frame);
        Store.Read(view =>
        {
            var charged = work.CreateView(view, grant);
            var original = frame.Original;
            RequireControlledBlobReceiver(charged, localPrincipalId, original);
            var blob = BlobControlledReadScope.Require(original);
            PartitionControlLifetimeCoverage.Require(frame.Coverage, blob.Partition,
                [original.Resource], Limits.MaxBatchBytes);
            var controlled = new PartitionControlResourceView(charged, blob.Partition,
                [original.Resource], frame.Coverage, Limits.MaxBatchBytes, work, grant);
            cancellationToken.ThrowIfCancellationRequested();
            AuthorizeOperation(controlled, original.Principal, original.Original!);
            ValidateCachedResult(controlled, original.Principal, original.Original!, original.OriginalOutcome!);
            return true;
        });
        work.Check();
        return null;
    }
}
