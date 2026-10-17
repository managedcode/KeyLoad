using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed partial class ResourceOwnerInventoryNativeRuntime
{
    private readonly ResourceRetirementReceiver retirement;

    internal Task<ResourceRetirementNativeReply> ExecuteRetirementAsync(ResourceRetirementNativeRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kind = request.Command ? NativeRequestWorkKind.CommandCapability : NativeRequestWorkKind.ReadCapability;
        return ExecuteAsync(request.RequestId, request.ExpiresAt, kind, ResourceRetirementNativeProtocol.Unavailable,
            caller => retirement.ExecuteAsync(request, caller), token);
    }
}
