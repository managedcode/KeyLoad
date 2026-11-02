using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Uses the current bounded execution owner, or the explicitly registered silo service owner.</summary>
internal static class ConnectionExecutionIdentity
{
    internal static Guid Resolve(IServiceProvider services)
    {
        if (((RequestContext.Get(GrainRequestStreamProtocol.ContextKey) /* KLD0041: The native Orleans RequestContext API uses object; validate or restore this call-local request identity value without changing its original type. */) is GrainRequestContextState state /* KLD0042: Validate the native Orleans RequestContext object as KeyLoad.Clustering.GrainRequestContextState before using call-local identity or owner state. */))
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
