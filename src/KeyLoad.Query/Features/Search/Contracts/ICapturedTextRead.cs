using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.Search;

/// <summary>Captures the original native reader and canonical snapshot under the same authorized query view.</summary>
internal interface ICapturedTextProjection
{
    ICapturedTextRead CaptureRead(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        SearchRequest request, ReadExecutionBudget budget);
}

/// <summary>Owns bounded original-cut records and the same reader through complete off-gate work and settlement.</summary>
internal interface ICapturedTextRead : IDisposable
{
    PrincipalRecord Principal { get; }
    ResourceDefinition Resource { get; }
    ITextProjectionLease OriginalProjection { get; }
    IKeyValueView CompleteSource();
    void VerifyTerminal(IReadOnlyList<EntityRef> selected);
}
