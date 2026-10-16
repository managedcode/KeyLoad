using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.Search;

internal static class AnnLifetimeProjection
{
    private const int InitialBytes = 0;
    private const int First = 0;
    private const string Missing = "A selected ANN document is unavailable.";
    private const string Exceeded = "The ANN result byte budget is exceeded.";

    internal static RankedDocument[] ProjectSelected(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord target, ResourceDefinition targetResource, AnnReadLifetimeMetadata source,
        SearchScore[] selected, ReadExecutionBudget budget)
    {
        var result = new RankedDocument[selected.Length];
        long projectedBytes = InitialBytes;
        for (var index = First; index < selected.Length; index++)
        {
            budget.Check();
            var document = budget.ReadRecord<DocumentRecord>(view, DocumentStorageKeys.RecordKey(selected[index].Reference))
                ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing);
            if (document.Deleted || !database.Authorization.CanReadRow(target, document.Access)
                || !database.Authorization.CanReadRow(source.Principal, document.Access))
            {
                throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing);
            }
            var projected = database.ProjectWithAuthorityIntersection(source.Principal, source.Definition,
                target, targetResource, document);
            var ranked = new RankedDocument(projected.Value, selected[index].Score);
            var bytes = budget.MeasureResult(ranked);
            if (bytes > budget.MaximumResultBytes - projectedBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, Exceeded);
            }
            projectedBytes += bytes;
            result[index] = ranked;
        }
        budget.CheckResult(result);
        return result;
    }
}
