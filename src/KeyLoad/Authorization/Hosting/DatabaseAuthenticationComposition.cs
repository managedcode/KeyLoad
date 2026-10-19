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
            && !request.Path.StartsWithSegments(ServerProtocol.HealthPrefix, StringComparison.OrdinalIgnoreCase)
            && !request.Path.StartsWithSegments(ServerProtocol.InternalPrefix, StringComparison.OrdinalIgnoreCase);

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
