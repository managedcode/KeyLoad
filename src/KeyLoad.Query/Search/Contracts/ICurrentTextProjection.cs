using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Search;

internal interface ICurrentTextProjection
{
    ITextProjectionLease AcquireCurrent(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget);
}
