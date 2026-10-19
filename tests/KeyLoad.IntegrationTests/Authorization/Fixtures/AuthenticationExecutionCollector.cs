using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class AuthenticationExecutionCollector
{
    private readonly Lock gate = new();
    private readonly AuthenticationExecutionEvent?[] events = new AuthenticationExecutionEvent?[AuthenticationExecutionProtocol.MaximumEvents];
    private int count;
    private bool overflow;

    internal IDatabaseAuthenticationExecutionObserver ForOwner(int owner) => new Observer(this, owner);
    internal int Position
    {
        get
        {
            lock (gate)
            {
                return count;
            }
        }
    }
    internal (AuthenticationExecutionEvent[] Events, bool Overflow) Read(int position)
    {
        lock (gate)
        {
            return (events.Skip(position).Take(count - position).Select(item => item!).ToArray(), overflow);
        }
    }
    private void Add(AuthenticationExecutionEvent observation)
    {
        lock (gate)
        {
            if (count == AuthenticationExecutionProtocol.MaximumEvents)
            {
                overflow = true;
                return;
            }
            events[count++] = observation;
        }
    }

    private sealed class Observer(AuthenticationExecutionCollector owner, int index)
        : IDatabaseAuthenticationExecutionObserver
    {
        public void Started(string httpRequestId, Guid authenticateRequestId)
            => owner.Add(new(index, httpRequestId, authenticateRequestId, AuthenticationExecutionEventKind.Started));
        public void Terminal(string httpRequestId, Guid authenticateRequestId, DatabasePhaseOutcome outcome)
            => owner.Add(new(index, httpRequestId, authenticateRequestId, AuthenticationExecutionEventKind.Terminal, outcome));
        public void Ticket(string httpRequestId, string runtimeSubject, DatabaseAuthenticationTicketSnapshot ticket)
            => owner.Add(new(index, httpRequestId, Guid.Empty, AuthenticationExecutionEventKind.Ticket,
                Subject: runtimeSubject, Ticket: ticket));
    }
}
