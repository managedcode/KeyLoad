using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class DatabaseAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder,
    IDatabaseAuthenticationExecutionObserver? executionObserver = null) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!DatabaseAuthenticationComposition.IsProtected(Context.Request))
        { return AuthenticateResult.NoResult(); }
        var reply = await DatabaseCredentialResolver.ReadAsync(Context, executionObserver).ConfigureAwait(false);
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
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        executionObserver?.Ticket(Context.TraceIdentifier, principal.Id,
            DatabaseAuthenticationTicketObservation.Capture(ticket, principal.Id));
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        => ServerErrorMiddleware.WriteAsync(Context, ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => ServerErrorMiddleware.WriteAsync(Context, ErrorCode.PermissionDenied,
            DatabaseAuthenticationComposition.Forbidden);
}
