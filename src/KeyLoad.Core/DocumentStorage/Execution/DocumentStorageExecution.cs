using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes DocumentStorageExecution operations against the caller's database context.</summary>
public static class DocumentStorageExecution
{
    internal static DocumentResult? GetDocumentBound(this DatabaseEngine engine, string principalId,
        GetDocumentRequest request, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding,
        Func<DatabaseEngine, IKeyValueView, string, PrincipalRecord> readPrincipal)
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
            var principal = readPrincipal(engine, view, principalId);
            return engine.ReadDocumentAtCut(view, principal, request.Reference, request.MinimumToken, cancellationToken);
        });
        return result;
    }
}
