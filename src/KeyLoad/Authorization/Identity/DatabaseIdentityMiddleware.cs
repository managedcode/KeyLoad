namespace KeyLoad.Server;

internal sealed class DatabaseIdentityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!DatabaseAuthenticationComposition.IsProtected(context.Request))
        {
            await next(context);
            return;
        }
        OperationResponseHeaders.Register(context);
        if (context.Request.Path.StartsWithSegments(McpFramingProtocol.Path, StringComparison.OrdinalIgnoreCase))
        {
            await McpHttpPipeline.RunAsync(context, next);
            return;
        }
        var principal = await DatabaseAuthenticationComposition.AuthenticateAsync(context);
        context.Items[ServerProtocol.PrincipalItem] = principal;
        await next(context);
        if (context.Response.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
        {
            await ServerErrorMiddleware.WriteAsync(context, ErrorCode.ResourceExhausted, ServerProtocol.BodyExceeded);
        }
    }

}
