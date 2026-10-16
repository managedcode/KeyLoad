using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal DocumentResult? GetDocumentBound(string principalId, EntityRef reference, CommitToken? minimumToken,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var work = new ReadExecutionBudget(OperationLimitsOptions, EvaluationClock, cancellationToken);
        return GetDocumentBound(principalId, new GetDocumentRequest(reference, minimumToken), work, binding);
    }

    internal DocumentResult? GetDocumentBound(string principalId, GetDocumentRequest request,
        ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        var cancellationToken = work.Cancellation;
        using var admission = AdmitQuery(cancellationToken);
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var principal = Principal(view, principalId, Clock.GetUtcNow());
            return ReadDocumentAtCut(view, principal, request.Reference, request.MinimumToken, cancellationToken);
        });
        work.CheckResult(result);
        return result;
    }

    internal OwnedDocumentReadResultV1 ReadOwnedDocumentV2Bound(string principalId, OwnedDocumentReadRequestV2 request,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        return ReadOwnedDocumentV2(principalId, request, work, grant);
    }

    internal DocumentResult? ReadControlledDocumentV2Bound(string localPrincipalId, PartitionControlDocumentReadFrameV2 frame,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant, IResourceLifetimeWorkBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        return ReadControlledDocumentV2(localPrincipalId, frame, work, grant, cancellationToken);
    }
}
