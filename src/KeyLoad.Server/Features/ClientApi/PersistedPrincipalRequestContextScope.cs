using KeyLoad.Orleans;
using ManagedCode.Orleans.Identity.Core.Constants;

namespace KeyLoad.Server;

internal sealed class PersistedPrincipalRequestContextScope : IDisposable
{
    private readonly object? previousPrincipal;
    private readonly object? previousState;
    private readonly bool hadPrincipal;
    private readonly bool hadState;
    private bool disposed;

    internal PersistedPrincipalRequestContextScope(IServiceProvider runtimeServices, PrincipalRecord? principal,
        Guid requestId, Guid commandId, CancellationToken cancellationToken)
    {
        var state = new GrainRequestContextState(requestId, commandId);
        var claims = principal is null ? null : GrainIdentityContext.CreatePrincipal(principal.Id);
        NativeRequestContextAdmission.Admit(runtimeServices, claims, state, cancellationToken);
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
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Restore(OrleansIdentityConstants.USER_CLAIMS, hadPrincipal, previousPrincipal);
        Restore(GrainRequestStreamProtocol.ContextKey, hadState, previousState);
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
