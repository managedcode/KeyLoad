namespace KeyLoad.Server;

internal sealed class GrainJsonResult(ReadOnlyMemory<byte> payload) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = ServerProtocol.JsonContentType;
        httpContext.Response.ContentLength = payload.Length;
        await httpContext.Response.Body.WriteAsync(payload, httpContext.RequestAborted).ConfigureAwait(false);
    }
}
