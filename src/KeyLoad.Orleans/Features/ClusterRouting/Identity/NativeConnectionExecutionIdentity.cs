using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Orleans;

/// <summary>Uses the current bounded execution owner, or the explicitly registered silo service owner.</summary>
internal static class NativeConnectionExecutionIdentity
{
    internal static Guid Resolve(IServiceProvider services)
    {
        if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is GrainRequestContextState state)
        {
            if (state.ConnectionId == Guid.Empty)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
            return state.ConnectionId;
        }
        return services.GetRequiredService<NativeConnectionOwnerIdentity>().Id;
    }
}
