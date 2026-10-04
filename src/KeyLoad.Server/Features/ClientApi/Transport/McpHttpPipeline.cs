using KeyLoad.Core;
using Microsoft.AspNetCore.Http.Features;

namespace KeyLoad.Server;

/// <summary>Bounds actual HTTP framing and resolves persisted authority before native stateless SDK processing.</summary>
internal static class McpHttpPipeline
{
    internal const string StateItem = "keyload-mcp-state";

    /// <summary>Owns the private replay and all leases until the native endpoint has finished draining.</summary>
    internal static async Task RunAsync(HttpContext context, RequestDelegate next)
    {
        var governor = context.RequestServices.GetRequiredService<HttpAdmissionGovernor>();
        var capacity = Capacity(context.Request, governor.Limits.MaxBodyBytes);
        using var state = new McpRequestState(governor, context.RequestServices.GetRequiredService<McpMemoryBudget>(),
            capacity, context.RequestAborted);
        var principal = await DatabaseCredentialResolver.ReadAsync(context).ConfigureAwait(false);
        state.Authenticate(principal, context.RequestAborted);
        var originalBody = context.Request.Body;
        McpFrameBody? pendingBody = null;
        try
        {
            var headers = McpTransportGuard.ReadHeaders(context.Request.Headers);
            context.Items[ServerProtocol.PrincipalItem] = state.Principal;
            context.Items[StateItem] = state;
            if (HttpMethods.IsPost(context.Request.Method))
            {
                SetBodyLimit(context, governor.Limits.MaxBodyBytes);
                pendingBody = await McpFrameBody.ReadAsync(originalBody, context.Request.ContentLength,
                    governor.Limits.MaxBodyBytes, context.RequestAborted).ConfigureAwait(false);
                McpTransportGuard.Inspect(pendingBody.Bytes, headers);
                context.Request.Body = pendingBody.OpenReader();
                state.Attach(pendingBody);
                pendingBody = null;
            }
            else
            { state.Admit(null, context.RequestAborted); }
            await next(context).ConfigureAwait(false);
        }
        catch (KeyLoadException error) when (McpTransportDiagnostics.HasStage(error))
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(McpTransportGuard));
            McpTransportDiagnostics.Log(logger, error, context.Request.Headers);
            throw;
        }
        finally
        {
            context.Request.Body = originalBody;
            context.Items.Remove(StateItem);
            pendingBody?.Dispose();
        }
    }

    private static int Capacity(HttpRequest request, int maximumBytes)
    {
        if (!HttpMethods.IsPost(request.Method))
        { return 0; }
        if (request.ContentLength is < 0 || request.ContentLength > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerProtocol.BodyExceeded); }
        return checked((int)(request.ContentLength ?? maximumBytes));
    }

    private static void SetBodyLimit(HttpContext context, int maximumBytes)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
        { feature.MaxRequestBodySize = maximumBytes; }
    }
}
