using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal sealed class AnnMaintenanceChildCalls(DecodedGrainRequest parent, Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
    IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing, ILogger diagnostics, IResourceLifetimeWorkBinding binding)
{
    private readonly Func<INativeAnnMaintenance> borrowNative = services.GetRequiredService<Func<INativeAnnMaintenance>>();
    internal Guid ParentRequestId => parent.Envelope.RequestId;
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal DatabaseEngine Database => services.GetRequiredService<DatabaseEngine>();

    internal Task<AnnMaintenanceCapabilityResult> CapabilityAsync(AnnMaintenanceRequest request,
        AnnMaintenanceCapabilityKind kind, CancellationToken token, ProjectionBatch? page = null,
        CommitProjectionBatchRequest? intent = null, AnnMaintenanceResult? fullResult = null,
        ProjectionConsumerInfo? originalConfigure = null)
        => ReadAsync<AnnMaintenanceCapabilityRequest, AnnMaintenanceCapabilityResult>(GrainReadKind.AnnMaintenance,
            new(request, SessionId, kind, page, intent, fullResult, originalConfigure), token);

    internal Task<TResult> ReadAsync<TRequest, TResult>(GrainReadKind kind, TRequest request, CancellationToken token)
        where TResult : class
        => DispatchAsync<TResult>(NativeSerialization.Serialize(request), kind, null, Guid.Empty, token);

    internal Task<TResult> CommandAsync<TRequest, TResult>(OperationKind kind, TRequest request,
        Guid commandId, CancellationToken token) where TResult : class
        => DispatchAsync<TResult>(NativeSerialization.Serialize(request), null, kind, commandId, token);

    internal Task AbortAsync() => borrowNative().AbortAsync(SessionId);

    private Task<TResult> DispatchAsync<TResult>(byte[] payload, GrainReadKind? read,
        OperationKind? command, Guid commandId, CancellationToken token) where TResult : class
        => AnnMaintenanceChildDispatch.ExecuteAsync<TResult>(parent, executeChild, services, codec,
            clock, serializer, routing, diagnostics, binding, payload, read, command, commandId, token);
}
