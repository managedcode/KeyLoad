using System.Diagnostics;
using KeyLoad.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class AuthorizationTelemetryEndpoint(TestDatabase database, TimeProvider clock,
    Func<AuthorizationUnsafeSpanState> state, Action completed)
{
    internal async Task ExecuteAsync(HttpContext context)
    {
        context.Response.OnCompleted(() =>
        {
            completed();
            return Task.CompletedTask;
        });
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(AuthorizationTelemetryTestProtocol.LoggerCategory);
        using var scope = logger.BeginScope(AuthorizationTelemetryTestProtocol.ScopeCanary);
        if (Activity.Current is { } activity)
        { activity.TraceStateString = AuthorizationTelemetryTestProtocol.TraceStateCanary; }
        Activity.Current?.SetTag(AuthorizationTelemetryTestProtocol.CanaryTag, AuthorizationTelemetryTestProtocol.TagCanary);
        Activity.Current?.AddBaggage(AuthorizationTelemetryTestProtocol.BaggageKey, AuthorizationTelemetryTestProtocol.BaggageCanary);
        AuthorizationTelemetryUnsafeState.Apply(Activity.Current, state());
        context.Features.Get<IHttpMetricsTagsFeature>()?.Tags.Add(new(
            AuthorizationTelemetryTestProtocol.CanaryTag, AuthorizationTelemetryTestProtocol.TagCanary));
        var request = await context.Request.ReadFromJsonAsync<AdminResourcesRequest>(JsonDefaults.Options, context.RequestAborted)
            ?? throw new InvalidOperationException(nameof(AdminResourcesRequest));
        var authorization = context.Request.Headers.Authorization.ToString();
        var secret = authorization[AuthenticationPrefix.Length..];
        var principal = database.Database.Authenticate(secret, clock.GetUtcNow());
        try
        {
            var result = new AdminCatalogReader(database.Database).Read(principal, request, context.RequestAborted);
            AuthorizationTelemetryLogging.Healthy(logger, request.TenantId);
            await context.Response.WriteAsJsonAsync(result, JsonDefaults.Options, context.RequestAborted);
        }
        catch (KeyLoadException error)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, AuthorizationTelemetryTestProtocol.ExceptionCanary);
            if (logger.IsEnabled(LogLevel.Error))
            { AuthorizationTelemetryLogging.Denied(logger, request.AfterName, new InvalidOperationException(AuthorizationTelemetryTestProtocol.ExceptionCanary)); }
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new AuthorizationTelemetryFailure(error.Code, error.Message),
                JsonDefaults.Options, context.RequestAborted);
        }
    }
    private const string AuthenticationPrefix = "Bearer ";
}
