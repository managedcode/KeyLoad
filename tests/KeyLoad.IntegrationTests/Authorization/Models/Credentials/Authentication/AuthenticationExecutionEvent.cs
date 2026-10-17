using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record AuthenticationExecutionEvent(int Owner, string HttpRequestId,
    Guid AuthenticateRequestId, AuthenticationExecutionEventKind Kind,
    DatabasePhaseOutcome? Outcome = null, string? Subject = null,
    DatabaseAuthenticationTicketSnapshot? Ticket = null);
