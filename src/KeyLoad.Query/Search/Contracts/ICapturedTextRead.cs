using KeyLoad.Storage;

namespace KeyLoad.Query.Features.Search;

/// <summary>Owns bounded original-cut records and the same reader through complete off-gate work and settlement.</summary>
internal interface ICapturedTextRead : IDisposable
{
    PrincipalRecord Principal
    {
        get;
    }
    ResourceDefinition Resource
    {
        get;
    }
    ITextProjectionLease OriginalProjection
    {
        get;
    }
    IKeyValueView CompleteSource();
    void VerifyTerminal(IReadOnlyList<EntityRef> selected);
}
