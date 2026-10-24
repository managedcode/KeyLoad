namespace KeyLoad.Server;

/// <summary>Passive, borrowed observation of actual standard credential execution.</summary>
internal interface IDatabaseAuthenticationExecutionObserver
{
    void Started(string httpRequestId, Guid authenticateRequestId);
    void Terminal(string httpRequestId, Guid authenticateRequestId,
        global::KeyLoad.Diagnostics.ResourceExecution.DatabasePhaseOutcome outcome);
    void Ticket(string httpRequestId, string runtimeSubject, DatabaseAuthenticationTicketSnapshot ticket);
}
