using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Fixture-only one-shot hold of the unchanged real Kestrel Accept response.</summary>
internal sealed class RemoteTransferOriginalResponseGate(Guid originalCommandId, CancellationToken originalCaller)
{
    private readonly Lock gate = new();
    private readonly TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool selected;
    internal Task Held => held.Task;
    internal Task? OriginalProducer
    {
        get { lock (gate) { return field; } }
        private set { lock (gate) { field = value; } }
    }
    internal void Release() => released.TrySetResult();

    internal void Attach(WebApplication application)
    {
        if (originalCommandId == Guid.Empty)
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        application.Use(next => context => ExecuteAsync(context, next));
    }

    private async Task ExecuteAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path != RemoteDocumentProtocol.Path)
        { await next(context).ConfigureAwait(false); return; }
        var originalRequest = context.Request.Body;
        var body = await RemoteDocumentWire.ReadRequestAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
        var call = RemoteDocumentWire.DecodeCall(body).QueueTransfer;
        using var borrowedRequest = new MemoryStream(body, writable: false);
        var failures = new List<Exception>();
        context.Request.Body = borrowedRequest;
        try
        {
            if (call is null || !Select(call))
            { await ServerFailureObserver.ObserveAsync(() => next(context), failures).ConfigureAwait(false); }
            else
            {
                var original = HoldAsync(context, next, call);
                OriginalProducer = original;
                await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
            }
        }
        finally
        { ServerFailureObserver.Observe(() => context.Request.Body = originalRequest, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private bool Select(RemoteQueueTransferPeerCall call)
    {
        if (call.Stage != RemoteQueueTransferPeerStage.Accept || call.OriginalCommandId != originalCommandId)
        { return false; }
        lock (gate)
        {
            if (selected)
            { return false; }
            selected = true;
            return true;
        }
    }

    private async Task HoldAsync(HttpContext context, RequestDelegate next, RemoteQueueTransferPeerCall call)
    {
        if (call.MaximumReplyBytes <= 0 || call.MaximumReplyBytes > RemoteDocumentProtocol.MaximumBodyBytes)
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        var originalResponse = context.Response.Body;
        var failures = new List<Exception>();
        using var response = new RemoteTransferOriginalResponseBuffer(call.MaximumReplyBytes);
        context.Response.Body = response;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await next(context).ConfigureAwait(false);
                RequireOriginal(context, call, response);
                held.TrySetResult();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(originalCaller, context.RequestAborted);
                await released.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                response.Position = 0;
                await response.CopyToAsync(originalResponse, linked.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        { ServerFailureObserver.Observe(() => context.Response.Body = originalResponse, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void RequireOriginal(HttpContext context, RemoteQueueTransferPeerCall call,
        RemoteTransferOriginalResponseBuffer response)
    {
        if (context.Response.StatusCode != StatusCodes.Status200OK
            || context.Response.ContentType != RemoteDocumentProtocol.ContentType
            || context.Response.ContentLength != response.Length || !response.TryGetBuffer(out var bytes))
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        var reply = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(bytes.AsSpan());
        if (reply.RequestId != call.RequestId || reply.Nonce != call.Nonce || reply.Error is not null
            || reply.QueueTransfer is not
            {
                Stage: RemoteQueueTransferPeerStage.Accept, Receipt: null,
                OriginalOutcome.Result: { Error: null }
            } || reply.Result is not null || reply.Controlled is not null
            || reply.ControlledBlob is not null || reply.QueryLeaf is not null || reply.SearchLeaf is not null)
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        _ = RemoteDocumentWire.Signature(context.Response.Headers);
    }
}
