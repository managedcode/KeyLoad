using KeyLoad.Core.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal sealed class AnnMaintenanceChildCalls(DecodedGrainRequest parent,
    Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild, Func<Guid, Task> abort,
    IServiceProvider services, GrainRequestCodec codec, ILogger diagnostics, IResourceLifetimeWorkBinding binding)
{
    internal Guid ParentRequestId => parent.Envelope.RequestId;
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal int ProjectionPageBytes => services.GetRequiredService<IOptions<GrainRoutingOptions>>().Value.MaximumReplyBytes;

    internal Task<AnnMaintenanceCapabilityResult> CapabilityAsync(AnnMaintenanceRequest request,
        AnnMaintenanceCapabilityKind kind, CancellationToken token, ProjectionBatch? page = null,
        CommitProjectionBatchRequest? intent = null, AnnMaintenanceResult? fullResult = null,
        ProjectionConsumerInfo? originalConfigure = null)
        => ReadAsync<AnnMaintenanceCapabilityRequest, AnnMaintenanceCapabilityResult>(GrainReadKind.AnnMaintenance,
            new(request, SessionId, kind, page, intent, fullResult, originalConfigure), token);

    internal Task<TResult> ReadAsync<TRequest, TResult>(GrainReadKind kind, TRequest request, CancellationToken token)
        where TResult : class
        => DispatchAsync<TResult>(OrleansSerialization.Serialize(request), kind, null, Guid.Empty, token);

    internal Task<TResult> CommandAsync<TRequest, TResult>(OperationKind kind, TRequest request,
        Guid commandId, CancellationToken token) where TResult : class
        => DispatchAsync<TResult>(OrleansSerialization.Serialize(request), null, kind, commandId, token);

    internal Task AbortAsync() => abort(SessionId);

    private Task<TResult> DispatchAsync<TResult>(byte[] payload, GrainReadKind? read,
        OperationKind? command, Guid commandId, CancellationToken token) where TResult : class
        => AnnMaintenanceChildDispatch.ExecuteAsync<TResult>(parent, executeChild, services, codec,
            diagnostics, binding, payload, read, command, commandId, token);
}
