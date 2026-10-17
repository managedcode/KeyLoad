namespace KeyLoad.Server;

/// <summary>Passive, borrowed fixture observation of actual standard credential execution.</summary>
internal interface IDatabaseAuthenticationExecutionObserver
{
    void Started(string httpRequestId, Guid authenticateRequestId);
    void Terminal(string httpRequestId, Guid authenticateRequestId,
        global::KeyLoad.Diagnostics.Features.ResourceExecution.DatabasePhaseOutcome outcome);
    void Ticket(string httpRequestId, string nativeSubject, DatabaseAuthenticationTicketSnapshot ticket);
}
