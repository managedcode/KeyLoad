using KeyLoad.Core.Features.ResourceExecution;

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
}
