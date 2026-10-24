using System.Net;

namespace KeyLoad.IntegrationTests.ResourceExecution;

internal sealed class SlowBodyAdmissionReturnBody(byte[] body) : HttpContent
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Entered => entered.Task;
    internal Task Started => started.Task;
    internal Task Finished => finished.Task;
    internal bool Released => release.Task.IsCompleted;
    internal void Release() => release.TrySetResult();
    protected override bool TryComputeLength(out long length)
    {
        length = body.Length;
        return true;
    }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
    {
        entered.TrySetResult();
        try
        {
            await stream.WriteAsync(body.AsMemory(0, SlowBodyAdmissionReturnProtocol.FirstByte), token);
            await stream.FlushAsync(token);
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            await stream.WriteAsync(body.AsMemory(SlowBodyAdmissionReturnProtocol.FirstByte), token);
        }
        finally
        {
            finished.TrySetResult();
        }
    }
    protected override void Dispose(bool disposing)
    {
        Release();
        base.Dispose(disposing);
    }
}
