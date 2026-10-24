using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Bounds actual HTTP framing and resolves persisted authority before execution stateless SDK processing.</summary>
internal static class McpHttpPipeline
{
    internal const string StateItem = "keyload-mcp-state";

    /// <summary>Owns the private replay and all leases until the execution endpoint has finished draining.</summary>
    internal static async Task RunAsync(HttpContext context, RequestDelegate next)
    {
        var executionOptions = context.RequestServices.GetRequiredService<IOptions<McpExecutionOptions>>();
        var maximumBodyBytes = context.RequestServices.GetRequiredService<IOptions<ServerExecutionOptions>>().Value.MaximumBodyBytes;
        RequireBodyLength(context.Request, maximumBodyBytes);
        using var state = new McpRequestState(maximumBodyBytes, executionOptions);
        var originalBody = context.Request.Body;
        McpFrameBody? pendingBody = null;
        try
        {
            context.Items[StateItem] = state;
            _ = await DatabaseAuthenticationComposition.AuthenticateAsync(context);
            var headers = McpTransportGuard.ReadHeaders(context.Request.Headers, executionOptions);
            context.Items[ServerProtocol.PrincipalItem] = state.Principal;
            if (HttpMethods.IsPost(context.Request.Method))
            {
                SetBodyLimit(context, maximumBodyBytes);
                pendingBody = await McpFrameBody.ReadAsync(originalBody, context.Request.ContentLength,
                    maximumBodyBytes, executionOptions, context.RequestAborted);
                McpTransportGuard.Inspect(pendingBody.Bytes, headers);
                context.Request.Body = pendingBody.OpenReader();
                state.Attach(pendingBody);
                pendingBody = null;
            }
            else
            {
                state.Admit(default);
            }
            await next(context);
        }
        catch (KeyLoadException error) when (McpTransportDiagnostics.HasStage(error))
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(McpTransportGuard));
            McpTransportDiagnostics.Log(logger, error, context.Request.Headers, executionOptions);
            throw;
        }
        finally
        {
            context.Request.Body = originalBody;
            context.Items.Remove(StateItem);
            pendingBody?.Dispose();
        }
    }

    private static void RequireBodyLength(HttpRequest request, int maximumBytes)
    {
        if (HttpMethods.IsPost(request.Method) && (request.ContentLength is < 0 || request.ContentLength > maximumBytes))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ServerProtocol.BodyExceeded);
        }
    }

    private static void SetBodyLimit(HttpContext context, int maximumBytes)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
        {
            feature.MaxRequestBodySize = maximumBytes;
        }
    }
}
