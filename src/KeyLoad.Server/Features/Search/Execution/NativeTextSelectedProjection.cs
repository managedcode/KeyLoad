using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

/// <summary>Owns bootstrap disposal and borrows the partition-owned maintained reader service.</summary>
internal sealed class NativeTextSelectedProjection(ITextProjection bootstrap,
    ISelectedTextProjection maintained, INativeTextSharedReadAdmission sharedAdmission) : ITextProjection, ISelectedTextProjection
{
    public ITextProjectionLease Acquire(TextProjectionScope scope, ReadExecutionBudget budget)
    {
        var admission = sharedAdmission.EnterBootstrapRead();
        try
        { return new NativeTextSharedProjectionLease(bootstrap.Acquire(scope, budget), admission); }
        catch (Exception primary)
        {
            try
            { admission.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    public ITextProjectionLease AcquireSelected(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
        => maintained.AcquireSelected(view, principal, resource, request, budget);

    public void Dispose() => bootstrap.Dispose();
}
