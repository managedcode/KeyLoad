using KeyLoad.Core;
using KeyLoad.Query.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Search;

/// <summary>Owns bootstrap disposal and borrows the partition-owned maintained reader service.</summary>
internal sealed class TextSelectedProjection(ITextProjection bootstrap,
    ISelectedTextProjection maintained, ITextSharedReadAdmission sharedAdmission) : ITextProjection, ISelectedTextProjection
{
    public ITextProjectionLease Acquire(TextProjectionScope scope, ReadExecutionBudget budget)
    {
        var admission = sharedAdmission.EnterBootstrapRead();
        try
        {
            return new TextSharedProjectionLease(bootstrap.Acquire(scope, budget), admission);
        }
        catch (Exception primary)
        {
            try
            {
                admission.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    public ITextProjectionLease AcquireSelected(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
        => maintained.AcquireSelected(view, principal, resource, request, budget);

    public void Dispose() => bootstrap.Dispose();
}
