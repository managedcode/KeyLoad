using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes DocumentStorageExecution operations against the caller's database context.</summary>
public static class DocumentStorageExecution
{
    internal static DocumentResult? GetDocumentBound(this DatabaseEngine engine, string principalId, EntityRef reference, CommitToken? minimumToken, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var work = new ReadExecutionBudget(engine.OperationLimitsOptions, engine.EvaluationClock, cancellationToken);
        return engine.GetDocumentBound(principalId, new GetDocumentRequest(reference, minimumToken), work, binding);
    }

    internal static DocumentResult? GetDocumentBound(this DatabaseEngine engine, string principalId, GetDocumentRequest request, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        var cancellationToken = work.Cancellation;
        using var admission = engine.AdmitQuery(cancellationToken);
        var result = engine.Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var principal = engine.Principal(view, principalId, engine.Clock.GetUtcNow());
            return engine.ReadDocumentAtCut(view, principal, request.Reference, request.MinimumToken, cancellationToken);
        });
        work.CheckResult(result);
        return result;
    }
}
