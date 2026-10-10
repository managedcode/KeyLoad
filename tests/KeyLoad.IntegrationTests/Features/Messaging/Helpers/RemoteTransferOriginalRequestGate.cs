using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferOriginalRequestGate(Guid command, CancellationToken caller)
{
    private readonly Lock gate = new();
    private readonly TaskCompletionSource<RemoteTransferHeldWire> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool selected;
    private byte[]? expectedFault;
    private TaskCompletionSource<ErrorCode>? observed;
    internal Task<RemoteTransferHeldWire> Held => held.Task;
    internal Task? OriginalProducer
    {
        get { lock (gate) { return field; } }
        private set { lock (gate) { field = value; } }
    }
    internal void Release() => released.TrySetResult();

    internal Task<ErrorCode> ExpectFault(byte[] bytes)
    {
        lock (gate)
        {
            if (observed is { Task.IsCompleted: false })
            { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
            expectedFault = bytes;
            observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return observed.Task;
        }
    }

    internal void Attach(int receiver, WebApplication app)
        => app.Use(next => context => ExecuteAsync(receiver, context, next));

    private async Task ExecuteAsync(int receiver, HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path != RemoteDocumentProtocol.Path)
        { await next(context).ConfigureAwait(false); return; }
        var originalBody = context.Request.Body;
        var bytes = await RemoteDocumentWire.ReadRequestAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
        using var borrowed = new MemoryStream(bytes, writable: false);
        context.Request.Body = borrowed;
        var failures = new List<Exception>();
        try
        {
            if (Select(bytes))
            {
                var original = HoldAsync(context, next);
                OriginalProducer = original;
                held.TrySetResult(new(RemoteDocumentWire.DecodeCall(bytes), bytes,
                    RemoteDocumentWire.Signature(context.Request.Headers), receiver));
                await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
            }
            else
            { await ServerFailureObserver.ObserveAsync(() => InvokeAsync(context, next, bytes), failures).ConfigureAwait(false); }
        }
        finally { ServerFailureObserver.Observe(() => context.Request.Body = originalBody, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private bool Select(byte[] bytes)
    {
        lock (gate)
        {
            if (selected)
            { return false; }
            var call = RemoteDocumentWire.DecodeCall(bytes).QueueTransfer;
            if (call?.Stage != RemoteQueueTransferPeerStage.Accept || call.OriginalCommandId != command)
            { return false; }
            selected = true;
            return true;
        }
    }

    private async Task HoldAsync(HttpContext context, RequestDelegate next)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, context.RequestAborted);
        await released.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        await next(context).ConfigureAwait(false);
    }

    private async Task InvokeAsync(HttpContext context, RequestDelegate next, byte[] bytes)
    {
        try
        { await next(context).ConfigureAwait(false); }
        catch (KeyLoadException error)
        {
            lock (gate)
            {
                if (expectedFault is not null && bytes.AsSpan().SequenceEqual(expectedFault))
                { observed?.TrySetResult(error.Code); expectedFault = null; }
            }
            throw;
        }
    }
}
