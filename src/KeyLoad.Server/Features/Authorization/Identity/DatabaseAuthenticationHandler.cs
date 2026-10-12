using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class DatabaseAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!DatabaseAuthenticationComposition.IsProtected(Context.Request))
        { return AuthenticateResult.NoResult(); }
        var reply = await DatabaseCredentialResolver.ReadAsync(Context).ConfigureAwait(false);
        PrincipalRecord principal;
        if (Context.Items[McpHttpPipeline.StateItem] is McpRequestState state)
        {
            state.Authenticate(reply, Context.RequestAborted);
            principal = state.Principal;
        }
        else
        {
            principal = McpNativeAuthentication.ReadPrincipal(reply.Span,
                Context.RequestServices.GetRequiredService<IOptions<McpExecutionOptions>>(), Context.RequestAborted);
        }
        Context.Features.Set(new DatabaseAuthenticatedPrincipal(principal));
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, principal.Id)], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        => ServerErrorMiddleware.WriteAsync(Context, ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => ServerErrorMiddleware.WriteAsync(Context, ErrorCode.PermissionDenied,
            DatabaseAuthenticationComposition.Forbidden);
}
