using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineProjection
    : ITextProjection, ISelectedTextProjection, ICurrentTextProjection, ICapturedTextProjection
{
    private readonly ITextProjection original;
    private readonly ISelectedTextProjection selected;
    private readonly NativeTextOnlineMaintenanceService online;
    private readonly DatabaseEngine database;

    internal NativeTextOnlineProjection(ITextProjection original, ISelectedTextProjection selected,
        NativeTextOnlineMaintenanceService online, DatabaseEngine database)
    {
        this.selected = selected;
        this.online = online;
        this.database = database;
        this.original = original;
    }

    internal NativeTextOnlineProjection(Func<ITextProjection> acquire, ISelectedTextProjection selected,
        NativeTextOnlineMaintenanceService online, DatabaseEngine database)
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
        => NativeTextCapturedReadFactory.Capture(database, this, view, principal, resource, request, budget);

    public void Dispose() => original.Dispose();
}
