using KeyLoad.Core.ResourceExecution;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

/// <summary>One parent operation's maintenance children, issued under one child purpose and one session.</summary>
internal sealed class MaintenanceChildCalls(DecodedGrainRequest parent,
    Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild, Func<Guid, Task> abort,
    IServiceProvider services, GrainRequestCodec codec, ILogger diagnostics, IResourceLifetimeWorkBinding binding,
    string purpose, string interrupted)
{
    internal Guid ParentRequestId => parent.Envelope.RequestId;
    internal Guid SessionId { get; } = Guid.NewGuid();

    internal Task<TResult> ReadAsync<TRequest, TResult>(GrainReadKind kind, TRequest request, CancellationToken token)
        where TRequest : notnull where TResult : class
        => CallAsync<TRequest, TResult>(kind, null, Guid.Empty, request, token);

    internal Task<TResult> CommandAsync<TRequest, TResult>(OperationKind kind, TRequest request,
        Guid commandId, CancellationToken token) where TRequest : notnull where TResult : class
        => CallAsync<TRequest, TResult>(null, kind, commandId, request, token);

    internal Task<TResult> RunAsync<TResult>(Func<Task<TResult>> flow)
        => MaintenanceChildDispatch.RunThenAbortAsync(flow, AbortAsync);

    internal Task AbortAsync() => abort(SessionId);

    private Task<TResult> CallAsync<TRequest, TResult>(GrainReadKind? read, OperationKind? command, Guid commandId,
        TRequest request, CancellationToken token) where TRequest : notnull where TResult : class
        => MaintenanceChildDispatch.CallAsync<TResult>(parent, (actor, principal) => codec.CreateMaintenanceChild(purpose,
                actor, principal, read, command, commandId, request), commandId, executeChild,
            services, codec, diagnostics, binding, interrupted, token);
}
