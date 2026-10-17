using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.Search;

/// <summary>Captures the original native reader and canonical snapshot under the same authorized query view.</summary>
internal interface ICapturedTextProjection
{
    ICapturedTextRead CaptureRead(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        SearchRequest request, ReadExecutionBudget budget);
}
