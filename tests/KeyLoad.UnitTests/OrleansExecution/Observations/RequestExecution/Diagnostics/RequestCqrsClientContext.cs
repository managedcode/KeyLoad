using System.Security.Claims;
using KeyLoad.Clustering;
using ManagedCode.Orleans.Identity.Core.Constants;

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed class RequestCqrsClientContext : IDisposable
{
    private const string StateKey = GrainRequestStreamProtocol.ContextKey;
    private const string PrincipalKey = OrleansIdentityConstants.USER_CLAIMS;
    private readonly bool stateWasPresent;
    private readonly bool principalWasPresent;
    private readonly object? priorState;
    private readonly object? priorPrincipal;
    private bool disposed;

    private RequestCqrsClientContext(ClaimsPrincipal? principal, bool includePrincipal,
        GrainRequestContextState? state, bool includeState, bool presentNullPrincipal)
    {
        stateWasPresent = RequestContext.Keys.Contains(StateKey, StringComparer.Ordinal);
        principalWasPresent = RequestContext.Keys.Contains(PrincipalKey, StringComparer.Ordinal);
        priorState = RequestContext.Get(StateKey);
        priorPrincipal = RequestContext.Get(PrincipalKey);
        if (includeState)
        {
            RequestContext.Set(StateKey, state!);
        }
        else
        {
            RequestContext.Remove(StateKey);
        }

        if (presentNullPrincipal)
        {
            RequestContext.Set(PrincipalKey, null!);
        }
        else if (includePrincipal)
        {
            RequestContext.Set(PrincipalKey, principal!);
        }
        else
        {
            RequestContext.Remove(PrincipalKey);
        }
    }

    internal static RequestCqrsClientContext Set(ClaimsPrincipal? principal, bool includePrincipal,
        GrainRequestContextState? state, bool includeState = true, bool presentNullPrincipal = false)
        => new(principal, includePrincipal, state, includeState, presentNullPrincipal);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Restore(StateKey, stateWasPresent, priorState);
        Restore(PrincipalKey, principalWasPresent, priorPrincipal);
    }

    private static void Restore(string key, bool wasPresent, object? value)
    {
        if (wasPresent)
        {
            RequestContext.Set(key, value!);
        }
        else
        {
            RequestContext.Remove(key);
        }
    }
}
