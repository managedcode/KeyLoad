using KeyLoad.Core.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>One parent operation's maintenance children, issued under one child purpose and one session.</summary>
internal sealed class MaintenanceChildCalls(DecodedGrainRequest parent,
    Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild, Func<Guid, Task> abort,
    IServiceProvider services, GrainRequestCodec codec, ILogger diagnostics, IResourceLifetimeWorkBinding binding,
    string purpose, string interrupted)
{
    internal Guid ParentRequestId => parent.Envelope.RequestId;
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal int ProjectionPageBytes => services.GetRequiredService<IOptions<GrainRoutingOptions>>().Value.MaximumReplyBytes;

    internal Task<TResult> ReadAsync<TRequest, TResult>(GrainReadKind kind, TRequest request, CancellationToken token)
        where TRequest : notnull where TResult : class
        => CallAsync<TResult>(kind, null, Guid.Empty, request, token);

    internal Task<TResult> CommandAsync<TRequest, TResult>(OperationKind kind, TRequest request,
        Guid commandId, CancellationToken token) where TRequest : notnull where TResult : class
        => CallAsync<TResult>(null, kind, commandId, request, token);

    internal Task<TResult> RunAsync<TResult>(Func<Task<TResult>> flow)
        => MaintenanceChildDispatch.RunThenAbortAsync(flow, AbortAsync);

    internal Task AbortAsync() => abort(SessionId);

    private Task<TResult> CallAsync<TResult>(GrainReadKind? read, OperationKind? command, Guid commandId,
        object request, CancellationToken token) where TResult : class
        => MaintenanceChildDispatch.CallAsync<TResult>(parent, (actor, principal) => codec.CreateMaintenanceChild(purpose,
                actor, principal, read, command, commandId, request, parent.Envelope.ExpiresAt), commandId, executeChild,
            services, codec, diagnostics, binding, interrupted, token);
}
