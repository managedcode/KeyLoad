using KeyLoad.Core;
using KeyLoad.Query.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Search;

internal sealed class TextOnlineProjection
    : ITextProjection, ISelectedTextProjection, ICurrentTextProjection, ICapturedTextProjection
{
    private readonly ITextProjection original;
    private readonly ISelectedTextProjection selected;
    private readonly TextOnlineMaintenanceService online;
    private readonly DatabaseEngine database;

    internal TextOnlineProjection(ITextProjection original, ISelectedTextProjection selected,
        TextOnlineMaintenanceService online, DatabaseEngine database)
    {
        this.selected = selected;
        this.online = online;
        this.database = database;
        this.original = original;
    }

    internal TextOnlineProjection(Func<ITextProjection> acquire, ISelectedTextProjection selected,
        TextOnlineMaintenanceService online, DatabaseEngine database)
    {
        this.selected = selected;
        this.online = online;
        this.database = database;
        original = acquire();
    }

    public ITextProjectionLease Acquire(TextProjectionScope scope, ReadExecutionBudget budget)
        => original.Acquire(scope, budget);

    public ITextProjectionLease AcquireCurrent(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
        => online.AcquireCurrent(view, principal, resource, request, budget)
            ?? original.Acquire(TextProjectionLifecycle.CreateScope(database, principal, resource, request), budget);

    public ITextProjectionLease AcquireSelected(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
        => selected.AcquireSelected(view, principal, resource, request, budget);

    public ICapturedTextRead CaptureRead(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
        => TextCapturedReadFactory.Capture(database, this, view, principal, resource, request, budget);

    public void Dispose() => original.Dispose();
}
