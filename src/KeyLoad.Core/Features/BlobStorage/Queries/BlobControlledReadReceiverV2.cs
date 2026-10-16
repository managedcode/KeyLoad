using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal object? ReadControlledBlobV2(string localPrincipalId, ControlledBlobReadFrameV2 frame,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant, CancellationToken cancellationToken)
    {
        work.Check();
        work.MeasureResult(frame);
        if (frame is null || frame.Original is null || frame.Original.Purpose == ControlledBlobReadPurpose.Outcome)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, PartitionMoveProtocol.Invalid);
        }
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view, grant);
            var original = frame.Original;
            RequireControlledBlobReceiver(charged, localPrincipalId, original);
            var blob = BlobControlledReadScope.Require(original);
            PartitionControlLifetimeCoverage.Require(frame.Coverage, blob.Partition,
                [original.Resource], Limits.MaxBatchBytes);
            BindAuthenticatedReadResourceLifetime(charged, blob.Partition, original.Resource,
                frame.Coverage.Lifetimes.Single());
            var controlled = new PartitionControlResourceView(charged, blob.Partition,
                [original.Resource], frame.Coverage, Limits.MaxBatchBytes, work, grant);
            cancellationToken.ThrowIfCancellationRequested();
            return ReadControlledBlobData(controlled, original);
        });
        work.MeasureResult(result);
        work.Check();
        return result;
    }
}
