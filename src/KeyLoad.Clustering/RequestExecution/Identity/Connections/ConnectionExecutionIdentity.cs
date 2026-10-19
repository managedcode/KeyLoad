using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Uses the current bounded execution owner, or the explicitly registered silo service owner.</summary>
internal static class ConnectionExecutionIdentity
{
    internal static Guid Resolve(IServiceProvider services)
    {
        if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is GrainRequestContextState state)
        {
            if (state.ConnectionId == Guid.Empty)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }
            return state.ConnectionId;
        }
        return services.GetRequiredService<ConnectionOwnerIdentity>().Id;
    }
}
