using System.Security.Claims;
using KeyLoad.Core.RuntimeJournaling.Identity;
using ManagedCode.Orleans.Identity.Core.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal sealed class GrainRequestIdentityScope : IDisposable
{
    private readonly object? previousPrincipal;
    private readonly object? previousState;
    private readonly object? previousPlacement;
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
        ArgumentNullException.ThrowIfNull(owner);
        GrainIdentityContext.Validate(request, request.RequestId);
        var state = RequestContext.Get(GrainRequestStreamProtocol.ContextKey) as GrainRequestContextState
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        var claims = RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS) as ClaimsPrincipal;
        return new(claims, state, owner, cancellationToken);
    }

    private GrainRequestIdentityScope(ClaimsPrincipal? claims,
        GrainRequestContextState state, global::Orleans.Runtime.SiloAddress? placement,
        CancellationToken cancellationToken)
    {
        RequestContextAdmission.Admit(claims, state, cancellationToken);
        placementSelected = placement is not null;
        previousPlacement = RequestContext.Get(PlacementKey);
        hadPlacement = RequestContext.Keys.Contains(PlacementKey, StringComparer.Ordinal);
        previousPrincipal = RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS);
        previousState = RequestContext.Get(GrainRequestStreamProtocol.ContextKey);
        hadPrincipal = RequestContext.Keys.Contains(OrleansIdentityConstants.USER_CLAIMS, StringComparer.Ordinal);
        hadState = RequestContext.Keys.Contains(GrainRequestStreamProtocol.ContextKey, StringComparer.Ordinal);
        if (claims is null)
        {
            RequestContext.Remove(OrleansIdentityConstants.USER_CLAIMS);
        }
        else
        {
            RequestContext.Set(OrleansIdentityConstants.USER_CLAIMS, claims);
        }

        RequestContext.Set(GrainRequestStreamProtocol.ContextKey, state);
        if (placement is not null)
        {
            RequestContext.Set(PlacementKey, placement);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Restore(OrleansIdentityConstants.USER_CLAIMS, hadPrincipal, previousPrincipal);
        Restore(GrainRequestStreamProtocol.ContextKey, hadState, previousState);
        if (placementSelected)
        {
            Restore(PlacementKey, hadPlacement, previousPlacement);
        }
        disposed = true;
    }

    private static void Restore(string key, bool present, object? value)
    {
        if (present)
        {
            // The execution API accepts null at runtime: preserve an existing present-null value exactly.
            RequestContext.Set(key, value!);
        }
        else
        {
            RequestContext.Remove(key);
        }
    }
}
