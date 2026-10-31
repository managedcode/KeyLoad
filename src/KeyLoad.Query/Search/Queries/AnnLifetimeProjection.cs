using KeyLoad.Core;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query.Search;

internal static class AnnLifetimeProjection
{
    private const int First = 0;
    private const string Missing = "A selected ANN document is unavailable.";

    internal static RankedDocument[] ProjectSelected(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord target, ResourceDefinition targetResource, AnnReadLifetimeMetadata source,
        SearchScore[] selected, ReadExecutionBudget budget)
    {
        var result = new RankedDocument[selected.Length];
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
            result[index] = new RankedDocument(projected.Value, selected[index].Score);
        }
        return result;
    }
}
