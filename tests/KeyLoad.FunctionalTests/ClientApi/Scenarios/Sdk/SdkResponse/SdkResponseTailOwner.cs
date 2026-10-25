using System.Collections.Concurrent;
using KeyLoad.Client;
using ManagedCode.Communication;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>Retains original real HTTP producers; the execution database and transport are borrowed.</summary>
internal sealed class SdkResponseTailOwner(TestDatabase database)
{
    private readonly TaskCompletionSource withholding = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int requests;
    internal SdkResponseAdmissionOwner Original
    {
        get;
    } = new(database, contentLength: false);
    internal ConcurrentQueue<Task> Producers
    {
        get;
    } = new();
    internal Task Withholding => withholding.Task;
    internal bool WasReleased => release.Task.IsCompleted;
    internal int RequestCount => Volatile.Read(ref requests);
    internal OperationCanceledException? OriginalAbort
    {
        get;
        private set;
    }
    internal Task<Result<CommitReceipt>>? SdkRequest
    {
        get;
        private set;
    }
    internal void Release() => release.TrySetResult();

    internal Task<Result<CommitReceipt>> SendAsync(KeyLoadClient client, CancellationToken token)
    {
        if (SdkRequest is not null)
        {
            throw new InvalidOperationException(SdkResponseTailProtocol.AlreadyRetained);
        }
        return SdkRequest = client.CommitAsync(Original.Command, token);
    }

    internal Task HandleAsync(HttpContext context)
    {
        var original = HandleCoreAsync(context);
        Producers.Enqueue(original);
        return original;
    }

    private async Task HandleCoreAsync(HttpContext context)
    {
        var number = Interlocked.Increment(ref requests);
        try
        {
            await Original.HandleAsync(context);
            if (number != SdkResponseTailProtocol.FirstRequest)
            {
                return;
            }
            withholding.TrySetResult();
            var probe = System.Text.Encoding.UTF8.GetBytes(SdkResponseTailProtocol.WhitespaceProbe);
            await context.Response.Body.WriteAsync(probe, context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await release.Task.WaitAsync(context.RequestAborted);
        }
        catch (OperationCanceledException failure) when (number == SdkResponseTailProtocol.FirstRequest
            && context.RequestAborted.IsCancellationRequested)
        {
            OriginalAbort = failure;
        }
    }
}
