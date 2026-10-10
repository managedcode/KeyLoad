using KeyLoad.Orleans;
using ManagedCode.Orleans.Identity.Core.Constants;

namespace KeyLoad.Server;

internal static class NativeDiscoveryOmissionContext
{
    internal static bool HasForeground(IHttpContextAccessor accessor)
    {
        var context = accessor.HttpContext;
        return context is not null && !context.RequestAborted.IsCancellationRequested
            && RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS) is null
            && RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is GrainRequestContextState state
            && state.RequestId != Guid.Empty && state.CommandId == Guid.Empty
            && context.Features.Get<ServerConnectionFeature>() is { } connection
            && state.ConnectionId == connection.Id
            && (DocumentApi.IsCommandPath(context.Request.Path) || context.Request.Path == McpFramingProtocol.Path);
    }
    internal static NativeDiscoveryOmissionWitness? Read(IHttpContextAccessor accessor,
        NativeDiscoveryOmissionArm arm, string localVoter, string targetVoter, Guid nonce, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var context = accessor.HttpContext;
        if (context is null || localVoter != arm.SourceVoter || targetVoter != arm.TargetVoter
            || RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS) is not null
            || RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState state
            || state.RequestId == Guid.Empty || state.CommandId != Guid.Empty
            || context.Features.Get<ServerConnectionFeature>() is not { } connection
            || state.ConnectionId != connection.Id || !Route(context.Request.Path, arm.Route))
        { return null; }
        context.RequestAborted.ThrowIfCancellationRequested();
        return new(NativeDiscoveryOmissionProtocol.Version, arm.SessionId, arm.ArmId,
            arm.SourceVoter, arm.TargetVoter, arm.Route, context.TraceIdentifier, connection.Id,
            state.RequestId, state.CommandId, nonce, NativeDiscoveryOmissionProtocol.SourceStage,
            null, null);
    }
    private static bool Route(PathString path, string route) => route switch
    {
        NativeDiscoveryOmissionProtocol.SdkRoute => DocumentApi.IsCommandPath(path),
        NativeDiscoveryOmissionProtocol.McpRoute => path == McpFramingProtocol.Path,
        _ => false
    };
}
