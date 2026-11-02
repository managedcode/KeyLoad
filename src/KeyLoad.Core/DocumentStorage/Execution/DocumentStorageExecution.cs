using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes DocumentStorageExecution operations against the caller's database context.</summary>
public static class DocumentStorageExecution
{
    internal static DocumentResult? GetDocumentBound(this DatabaseEngine engine, string principalId, EntityRef reference, CommitToken? minimumToken, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        var work = new ReadExecutionBudget(engine.EvaluationClock, cancellationToken);
        return engine.GetDocumentBound(principalId, new GetDocumentRequest(reference, minimumToken), work, binding);
    }

    internal static DocumentResult? GetDocumentBound(this DatabaseEngine engine, string principalId, GetDocumentRequest request, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(work) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        work.AttachResourceLifetimeBinding(binding);
        var cancellationToken = work.Cancellation;
        using var admission = engine.AdmitQuery(cancellationToken);
        var result = engine.Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var principal = engine.Principal(view, principalId, engine.Clock.GetUtcNow());
            return engine.ReadDocumentAtCut(view, principal, request.Reference, request.MinimumToken, cancellationToken);
        });
        return result;
    }
}
