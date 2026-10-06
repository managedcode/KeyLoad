using KeyLoad.Core;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class DatabaseIdentityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (AdminStaticAssets.IsPublicRequest(context.Request)
            || context.Request.Path.StartsWithSegments(ServerProtocol.HealthPrefix, StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments(ServerProtocol.InternalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        OperationResponseHeaders.Register(context);
        if (context.Request.Path.StartsWithSegments(McpFramingProtocol.Path, StringComparison.OrdinalIgnoreCase))
        {
            await McpHttpPipeline.RunAsync(context, next).ConfigureAwait(false);
            return;
        }
        using var incoming = context.RequestServices.GetRequiredService<HttpAdmissionGovernor>().Begin(
            context.Request.Path.Value ?? string.Empty, context.Request.ContentLength, context.RequestAborted);
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } body)
        {
            body.MaxRequestBodySize = incoming.MaxBodyBytes;
        }
        var reply = await DatabaseCredentialResolver.ReadAsync(context).ConfigureAwait(false);
        var principal = McpNativeAuthentication.ReadPrincipal(payload: reply.Span, cancellationToken: context.RequestAborted,
            options: context.RequestServices.GetRequiredService<IOptions<McpExecutionOptions>>());
        incoming.Bind(principal, context.RequestAborted);
        context.Items[ServerProtocol.PrincipalItem] = principal;
        await next(context).ConfigureAwait(false);
        if (context.Response.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
        {
            await ServerErrorMiddleware.WriteAsync(context, ErrorCode.ResourceExhausted, ServerProtocol.BodyExceeded).ConfigureAwait(false);
        }
    }

}
