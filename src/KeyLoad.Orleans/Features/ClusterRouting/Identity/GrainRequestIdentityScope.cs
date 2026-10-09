using ManagedCode.Orleans.Identity.Core.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Orleans;

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
    {
        placement ??= runtimeServices.GetService<IPhysicalRequestPlacement>()?.Current;
        var state = new GrainRequestContextState(requestId, commandId, connectionId);
        var claims = principal is null ? null : GrainIdentityContext.CreatePrincipal(principal.Id);
        NativeRequestContextAdmission.Admit(runtimeServices, claims, state, cancellationToken, placement);
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
        { RequestContext.Set(PlacementKey, placement); }
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
        { Restore(PlacementKey, hadPlacement, previousPlacement); }
        disposed = true;
    }

    private static void Restore(string key, bool present, object? value)
    {
        if (present)
        {
            // The native API accepts null at runtime: preserve an existing present-null value exactly.
            RequestContext.Set(key, value!);
        }
        else
        {
            RequestContext.Remove(key);
        }
    }
}
