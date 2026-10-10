using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextCapturedRead : ICapturedTextRead
{
    private const int NoFailures = 0;
    private const string AlreadySettled = "The original search capture is already settled.";
    private const string NotSettled = "The original search capture has not settled.";

    private readonly DatabaseEngine database;
    private readonly SearchRequest request;
    private readonly ReadExecutionBudget budget;
    private readonly ReadExecutionBudgetReadGrant grant;
    private readonly NativeSearchReadAuthority authority;
    private readonly ZoneTreeReadCutLease native;
    private readonly ZoneTreeCapturedReadBuilder builder;
    private ZoneTreeCapturedReadView? captured;
    private bool nativeJoined;
    private bool disposed;

    internal NativeTextCapturedRead(DatabaseEngine database, SearchRequest request, ReadExecutionBudget budget,
        ReadExecutionBudgetReadGrant grant, NativeSearchReadAuthority authority, ZoneTreeReadCutLease native,
        ITextProjectionLease projection, PrincipalRecord principal, ResourceDefinition resource)
    {
        this.database = database;
        this.request = request;
        this.budget = budget;
        this.grant = grant;
        this.authority = authority;
        this.native = native;
        OriginalProjection = projection;
        Principal = principal;
        Resource = resource;
        builder = new(native, Admit);
    }

    public PrincipalRecord Principal { get; }
    public ResourceDefinition Resource { get; }
    public ITextProjectionLease OriginalProjection { get; }

    private void Admit(long bytes, int records)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!grant.IsCompleted)
        { budget.ImportReadGrant(grant, bytes, records); }
        else
        { budget.ChargeBytes(bytes); }
    }
}
