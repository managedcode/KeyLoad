using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace KeyLoad.Server;

internal static class DatabaseAuthenticationComposition
{
    internal const string Scheme = "KeyLoad.Database";
    internal const string Forbidden = "The authenticated request is not authorized.";

    internal static void Register(IServiceCollection services)
    {
        services.AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, DatabaseAuthenticationHandler>(Scheme, configureOptions: null);
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build());
    }

    internal static bool IsProtected(HttpRequest request)
        => !AdminStaticAssets.IsPublicRequest(request)
            && !IsPublicFrameworkRequest(request)
            && !request.Path.StartsWithSegments(ServerProtocol.HealthPrefix, StringComparison.OrdinalIgnoreCase);

    private const string CircuitPath = "/_blazor";
    private const string FrameworkPath = "/_framework";
    private const string FluentAssetPath = "/_content/Microsoft.FluentUI.AspNetCore.Components";

    private static bool IsPublicFrameworkRequest(HttpRequest request) =>
        request.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null
        && (request.Path.StartsWithSegments(CircuitPath, StringComparison.Ordinal)
            || (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                && (request.Path.StartsWithSegments(FrameworkPath, StringComparison.Ordinal)
                    || request.Path.StartsWithSegments(FluentAssetPath, StringComparison.Ordinal)));

    internal static async Task<PrincipalRecord> AuthenticateAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(Scheme);
        if (!result.Succeeded || result.Principal is null
            || context.Features.Get<DatabaseAuthenticatedPrincipal>() is not { } admitted
            || !string.Equals(result.Principal.FindFirstValue(ClaimTypes.NameIdentifier),
                admitted.Principal.Id, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        context.User = result.Principal;
        return admitted.Principal;
    }
}
