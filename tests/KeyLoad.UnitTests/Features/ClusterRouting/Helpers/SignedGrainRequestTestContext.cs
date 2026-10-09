using KeyLoad.Orleans;
using ManagedCode.Orleans.Identity.Core.Constants;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Supplies the closed context for direct real-database leaf tests, without replacing persisted authorization.</summary>
internal static class SignedGrainRequestTestContext
{
    private static readonly Guid ExecutionOwnerId = Guid.NewGuid();
    internal static async Task<GrainOperationReply> ExecuteAsync(GrainCommandExecutor executor,
        DecodedGrainRequest request, string actorKey, CancellationToken cancellationToken)
    {
        var principalKey = OrleansIdentityConstants.USER_CLAIMS;
        var stateKey = GrainRequestStreamProtocol.ContextKey;
        var hadPrincipal = RequestContext.Keys.Contains(principalKey, StringComparer.Ordinal);
        var hadState = RequestContext.Keys.Contains(stateKey, StringComparer.Ordinal);
        var previousPrincipal = RequestContext.Get(principalKey);
        var previousState = RequestContext.Get(stateKey);
        try
        {
            if (request.Envelope.PrincipalId is { } subject)
            {
                RequestContext.Set(principalKey, GrainIdentityContext.CreatePrincipal(subject));
            }
            else
            {
                RequestContext.Remove(principalKey);
            }
            RequestContext.Set(stateKey, new GrainRequestContextState(request.Envelope.RequestId, request.Envelope.CommandId, ExecutionOwnerId));
            return await executor.ExecuteAsync(request, actorKey, cancellationToken);
        }
        finally
        {
            Restore(principalKey, hadPrincipal, previousPrincipal);
            Restore(stateKey, hadState, previousState);
        }
    }

    private static void Restore(string key, bool existed, object? value)
    {
        if (existed)
        {
            RequestContext.Set(key, value!);
        }
        else
        {
            RequestContext.Remove(key);
        }
    }
}
