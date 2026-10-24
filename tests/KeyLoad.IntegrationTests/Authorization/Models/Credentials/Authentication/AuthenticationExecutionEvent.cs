using KeyLoad.Diagnostics.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record AuthenticationExecutionEvent(int Owner, string HttpRequestId,
    Guid AuthenticateRequestId, AuthenticationExecutionEventKind Kind,
    DatabasePhaseOutcome? Outcome = null, string? Subject = null,
    DatabaseAuthenticationTicketSnapshot? Ticket = null);
