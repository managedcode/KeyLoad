using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal enum AuthenticationExecutionEventKind { Started, Terminal, Ticket }

internal sealed record AuthenticationExecutionEvent(int Owner, string HttpRequestId,
    Guid AuthenticateRequestId, AuthenticationExecutionEventKind Kind,
    DatabasePhaseOutcome? Outcome = null, string? Subject = null,
    DatabaseAuthenticationTicketSnapshot? Ticket = null);
