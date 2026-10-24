using System.Net;

namespace KeyLoad.IntegrationTests.ResourceExecution;

internal sealed class EarlyAdmissionQualificationBody(byte[] body) : HttpContent
{
    private const int FirstByte = 1;
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Started => started.Task;
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
        await stream.WriteAsync(body.AsMemory(0, FirstByte), token);
        await stream.FlushAsync(token);
        started.TrySetResult();
        await release.Task.WaitAsync(token);
        await stream.WriteAsync(body.AsMemory(FirstByte), token);
    }
    protected override void Dispose(bool disposing)
    {
        Release();
        base.Dispose(disposing);
    }
}
