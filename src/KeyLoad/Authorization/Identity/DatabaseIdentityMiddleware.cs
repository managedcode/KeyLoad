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
        ((context.Items /* KLD0041: Publish the verified PrincipalRecord reference in ASP.NET HttpContext.Items for this request's downstream authorization and file-storage access. */)[ServerProtocol.PrincipalItem] /* KLD0041: Publish the verified PrincipalRecord reference in ASP.NET HttpContext.Items for this request's downstream authorization and file-storage access. */) = principal;
        await next(context);
        if (context.Response.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
        {
            await ServerErrorMiddleware.WriteAsync(context, ErrorCode.ResourceExhausted, ServerProtocol.BodyExceeded);
        }
    }

}
