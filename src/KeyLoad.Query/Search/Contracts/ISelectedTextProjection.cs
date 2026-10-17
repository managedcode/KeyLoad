using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.Search;

/// <summary>Acquires an explicitly maintained native generation under its canonical read owner.</summary>
public interface ISelectedTextProjection
{
    /// <summary>Acquires the selected generation without implicit build or checkpoint effects.</summary>
    /// <param name="view">Original owned canonical read view.</param>
    /// <param name="principal">Fresh persisted caller.</param>
    /// <param name="resource">Fresh admitted resource.</param>
    /// <param name="request">Original search request including exact selection.</param>
    /// <param name="budget">Original shared token, deadline and work budget.</param>
    /// <returns>A native lease settled before the canonical view leaves ownership.</returns>
    ITextProjectionLease AcquireSelected(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget);
}
