using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.Search;

internal static class SelectedTextProjectionAdmission
{
    private const string Missing = "The selected native text generation is unavailable; explicitly restore it.";

    internal static ITextProjectionLease? Acquire(DatabaseEngine database, ITextProjection? provider,
        IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, SearchRequest request,
        ReadExecutionBudget budget)
    {
        budget.Check();
        if (request.TextIndex is not null)
        {
            var selected = provider as ISelectedTextProjection
                ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing);
            return selected.AcquireSelected(view, principal, resource, request, budget);
        }
        if (provider is ICurrentTextProjection current)
        { return current.AcquireCurrent(view, principal, resource, request, budget); }
        return provider?.Acquire(TextProjectionLifecycle.CreateScope(database, principal, resource, request), budget);
    }
}
