using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal DocumentResult? ReadControlledDocumentV2(string localPrincipalId, PartitionControlDocumentReadFrameV2 frame,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant, CancellationToken token)
    {
        work.Check();
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view, grant);
            RequireControlledDocumentReadReceiver(charged, localPrincipalId, frame.Original);
            PartitionControlLifetimeCoverage.Require(frame.Coverage, frame.Original.Reference.Partition,
                [frame.Original.Resource], Limits.MaxBatchBytes);
            var controlled = new PartitionControlResourceView(charged, frame.Original.Reference.Partition,
                [frame.Original.Resource], frame.Coverage, Limits.MaxBatchBytes, work, grant);
            return ReadDocumentAtCut(controlled, frame.Original.Principal, frame.Original.Reference,
                frame.Original.MinimumToken, token);
        });
        work.CheckResult(result);
        return result;
    }
}
