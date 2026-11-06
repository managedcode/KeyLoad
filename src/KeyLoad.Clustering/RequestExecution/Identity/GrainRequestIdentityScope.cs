using System.Security.Claims;
using KeyLoad.Core.RuntimeJournaling.Identity;
using ManagedCode.Orleans.Identity.Core.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal sealed class GrainRequestIdentityScope : IDisposable
{
    private readonly object? /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */ previousPrincipal;
    private readonly object? /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */ previousState;
    private readonly object? /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */ previousPlacement;
    private readonly bool hadPlacement;
    private readonly bool placementSelected;
    private const string PlacementKey = global::Orleans.Runtime.Placement.IPlacementDirector.PlacementHintKey;
    private readonly bool hadPrincipal;
    private readonly bool hadState;
    private bool disposed;

    internal GrainRequestIdentityScope(IServiceProvider runtimeServices, PrincipalRecord? principal,
        Guid requestId, Guid commandId, CancellationToken cancellationToken,
        global::Orleans.Runtime.SiloAddress? placement = null, Guid connectionId = default)
        : this(principal is null ? null : GrainIdentityContext.CreatePrincipal(principal.Id),
            new(requestId, commandId, connectionId, runtimeServices.GetService<ILocalSiloDetails>()?.SiloAddress),
            placement, cancellationToken)
    {
    }

    internal static GrainRequestIdentityScope ForRuntimeJournal(IServiceProvider runtimeServices,
        Guid requestId, Guid commandId, Guid connectionId, CancellationToken cancellationToken)
        => new(GrainIdentityContext.CreatePrincipal(RuntimeJournalIdentity.ProtectedPrincipalId),
            new(requestId, commandId, connectionId, runtimeServices.GetService<ILocalSiloDetails>()?.SiloAddress),
            null, cancellationToken);

    internal static GrainRequestIdentityScope ForChild(IServiceProvider services, GrainRequestEnvelope parent,
        Guid requestId, Guid commandId, CancellationToken cancellationToken)
    {
        GrainIdentityContext.Validate(parent, parent.RequestId);
        var principal = parent.PrincipalId
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        return new(GrainIdentityContext.CreatePrincipal(principal),
            new(requestId, commandId, ConnectionExecutionIdentity.Resolve(services),
                services.GetService<ILocalSiloDetails>()?.SiloAddress), null, cancellationToken);
    }

    internal static GrainRequestIdentityScope ForPlacement(GrainRequestEnvelope request,
        SiloAddress owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner) /* KLD0041: BCL null validation accepts the owner SiloAddress reference through its object parameter. */;
        GrainIdentityContext.Validate(request, request.RequestId);
        var state = ((RequestContext.Get(GrainRequestStreamProtocol.ContextKey) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */) as GrainRequestContextState /* KLD0042: Validate the native Orleans RequestContext object as KeyLoad.Clustering.GrainRequestContextState? before using call-local identity or owner state. */)
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        var claims = ((RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */) as ClaimsPrincipal /* KLD0042: Validate the native Orleans RequestContext object as System.Security.Claims.ClaimsPrincipal? before using call-local identity or owner state. */);
        return new(claims, state, owner, cancellationToken);
    }

    private GrainRequestIdentityScope(ClaimsPrincipal? claims,
        GrainRequestContextState state, global::Orleans.Runtime.SiloAddress? placement,
        CancellationToken cancellationToken)
    {
        RequestContextAdmission.Admit(claims, state, cancellationToken);
        placementSelected = placement is not null;
        (previousPlacement /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */) = (RequestContext.Get(PlacementKey) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */);
        hadPlacement = RequestContext.Keys.Contains(PlacementKey, StringComparer.Ordinal);
        (previousPrincipal /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */) = (RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */);
        (previousState /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */) = (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */);
        hadPrincipal = RequestContext.Keys.Contains(OrleansIdentityConstants.USER_CLAIMS, StringComparer.Ordinal);
        hadState = RequestContext.Keys.Contains(GrainRequestStreamProtocol.ContextKey, StringComparer.Ordinal);
        if (claims is null)
        {
            RequestContext.Remove(OrleansIdentityConstants.USER_CLAIMS);
        }
        else
        {
            RequestContext.Set(OrleansIdentityConstants.USER_CLAIMS, claims) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */;
        }

        RequestContext.Set(GrainRequestStreamProtocol.ContextKey, state) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */;
        if (placement is not null)
        {
            RequestContext.Set(PlacementKey, placement) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Restore(OrleansIdentityConstants.USER_CLAIMS, hadPrincipal, (previousPrincipal /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */)) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */;
        Restore(GrainRequestStreamProtocol.ContextKey, hadState, (previousState /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */)) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */;
        if (placementSelected)
        {
            Restore(PlacementKey, hadPlacement, (previousPlacement /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */)) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */;
        }
        disposed = true;
    }

    internal static void Restore(string key, bool present, object? /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */ value)
    {
        if (present)
        {
            // The execution API accepts null at runtime: preserve an existing present-null value exactly.
            RequestContext.Set(key, value!) /* KLD0041: Preserve and restore the exact previous Orleans RequestContext value, including an unknown type or a present null, across this call-local scope. */;
        }
        else
        {
            RequestContext.Remove(key);
        }
    }
}
