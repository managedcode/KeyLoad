using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal enum FirstRequestStage
{
    WaitingForHandler,
    HandlerEntered,
    WritingFirstChunk,
    FirstChunkWritten,
    FlushingFirstChunk,
    FirstChunkFlushed,
    WaitingForSecondWriteRelease,
    WritingSecondChunk,
    SecondChunkWritten,
    FlushingSecondChunk,
    SecondChunkFlushed,
    WaitingForRequestAborted,
    RequestAborted
}

internal sealed class MidBodyCancellationResponse(byte[] partialResponse, NodeStatus nextResponse, int chunkSize)
{
    private const string JsonContentType = "application/json";
    private int requestCount;
    private int stage = (int)FirstRequestStage.WaitingForHandler;

    public TaskCompletionSource FirstChunkWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondWriteReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource HandlerEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<(FirstRequestStage Stage, Exception Error)> HandlerFailure { get; }
        = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource FirstHandlerCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondHandlerEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondHandlerCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource RequestAborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public FirstRequestStage Stage => (FirstRequestStage)Volatile.Read(ref stage);

    public void ReleaseSecondWrite() => SecondWriteReleased.TrySetResult();

    public async Task HandleAsync(HttpContext context)
    {
        context.Response.ContentType = JsonContentType;
        var currentRequest = Interlocked.Increment(ref requestCount);
        if (currentRequest != 1)
        {
            SecondHandlerEntered.TrySetResult();
            try
            {
                await JsonSerializer.SerializeAsync(context.Response.Body, nextResponse, JsonDefaults.Options,
                    context.RequestAborted);
            }
            finally
            {
                SecondHandlerCompleted.TrySetResult();
            }
            return;
        }

        SetStage(FirstRequestStage.HandlerEntered);
        HandlerEntered.TrySetResult();
        try
        {
            SetStage(FirstRequestStage.WritingFirstChunk);
            await context.Response.Body.WriteAsync(partialResponse.AsMemory(0, chunkSize), context.RequestAborted);
            SetStage(FirstRequestStage.FirstChunkWritten);
            SetStage(FirstRequestStage.FlushingFirstChunk);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            SetStage(FirstRequestStage.FirstChunkFlushed);
            FirstChunkWritten.TrySetResult();
            SetStage(FirstRequestStage.WaitingForSecondWriteRelease);
            await SecondWriteReleased.Task;
            await WriteSecondChunkAsync(context);
            SetStage(FirstRequestStage.WaitingForRequestAborted);
            await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            SetStage(FirstRequestStage.RequestAborted);
            RequestAborted.TrySetResult();
        }
        catch (Exception exception)
        {
            HandlerFailure.TrySetResult((Stage, exception));
            throw;
        }
        finally
        {
            FirstHandlerCompleted.TrySetResult();
        }
    }

    private async Task WriteSecondChunkAsync(HttpContext context)
    {
        SetStage(FirstRequestStage.WritingSecondChunk);
        await context.Response.Body.WriteAsync(partialResponse.AsMemory(chunkSize, chunkSize), context.RequestAborted);
        SetStage(FirstRequestStage.SecondChunkWritten);
        SetStage(FirstRequestStage.FlushingSecondChunk);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        SetStage(FirstRequestStage.SecondChunkFlushed);
    }

    private void SetStage(FirstRequestStage next) => Volatile.Write(ref stage, (int)next);
}
