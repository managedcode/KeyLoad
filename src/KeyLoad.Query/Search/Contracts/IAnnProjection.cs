using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.Search;

internal interface IAnnProjection
{
    IAnnProjectionLease Acquire(IKeyValueView view, AnnProjectionSelection request,
        ReadExecutionBudget budget);
}
