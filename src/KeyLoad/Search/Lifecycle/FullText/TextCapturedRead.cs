using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Query.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server.Search;

internal sealed partial class TextCapturedRead : ICapturedTextRead
{
    private const int NoFailures = 0;
    private const string AlreadySettled = "The original search capture is already settled.";
    private const string NotSettled = "The original search capture has not settled.";

    private readonly DatabaseEngine database;
    private readonly SearchRequest request;
    private readonly ReadExecutionBudget budget;
    private readonly SearchReadAuthority authority;
    private readonly ZoneTreeReadCutLease execution;
    private readonly ZoneTreeCapturedReadBuilder builder;
    private ZoneTreeCapturedReadView? captured;
    private bool runtimeJoined;
    private bool disposed;

    internal TextCapturedRead(DatabaseEngine database, SearchRequest request, ReadExecutionBudget budget, SearchReadAuthority authority, ZoneTreeReadCutLease execution,
        ITextProjectionLease projection, PrincipalRecord principal, ResourceDefinition resource)
    {
        this.database = database;
        this.request = request;
        this.budget = budget;
        this.authority = authority;
        this.execution = execution;
        OriginalProjection = projection;
        Principal = principal;
        Resource = resource;
        builder = new(execution, static (_, _) => { });
    }

    public PrincipalRecord Principal
    {
        get;
    }
    public ResourceDefinition Resource
    {
        get;
    }
    public ITextProjectionLease OriginalProjection
    {
        get;
    }
}
