using System.Security.Claims;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class AuthenticationExecutionAssertions
{
    internal static async Task WindowAsync(AuthenticationExecutionCollector collector, int position,
        int actualHttpRequests, string? subject, bool entered = true)
    {
        await Assert.That(actualHttpRequests).IsGreaterThan(AuthenticationExecutionProtocol.NoEvents);
        var (events, overflow) = collector.Read(position);
        await Assert.That(overflow).IsFalse();
        var groups = events.GroupBy(item => (item.Owner, item.HttpRequestId)).ToArray();
        await Assert.That(groups.Length).IsEqualTo(entered ? actualHttpRequests : AuthenticationExecutionProtocol.NoEvents);
        foreach (var group in groups)
        {
            var starts = group.Where(item => item.Kind == AuthenticationExecutionEventKind.Started).ToArray();
            var terminals = group.Where(item => item.Kind == AuthenticationExecutionEventKind.Terminal).ToArray();
            var tickets = group.Where(item => item.Kind == AuthenticationExecutionEventKind.Ticket).ToArray();
            await Assert.That(starts.Length).IsEqualTo(AuthenticationExecutionProtocol.One);
            await Assert.That(terminals.Length).IsEqualTo(AuthenticationExecutionProtocol.One);
            await Assert.That(starts.Single().AuthenticateRequestId).IsNotEqualTo(Guid.Empty);
            await Assert.That(terminals.Single().AuthenticateRequestId).IsEqualTo(starts.Single().AuthenticateRequestId);
            await Assert.That(terminals.Single().Outcome is DatabasePhaseOutcome.Completed or DatabasePhaseOutcome.Faulted).IsTrue();
            await Assert.That(tickets.Length).IsEqualTo(subject is null ? AuthenticationExecutionProtocol.NoEvents : AuthenticationExecutionProtocol.One);
            if (subject is not null)
            {
                await Assert.That(terminals.Single().Outcome).IsEqualTo(DatabasePhaseOutcome.Completed);
                await TicketAsync(tickets.Single(), subject);
            }
        }
    }

    private static async Task TicketAsync(AuthenticationExecutionEvent observation, string expectedSubject)
    {
        await Assert.That(observation.Subject == expectedSubject).IsTrue();
        var ticket = observation.Ticket ?? throw new InvalidOperationException(AuthenticationExecutionProtocol.MissingEvent);
        await Assert.That(ticket.Scheme).IsEqualTo(DatabaseAuthenticationComposition.Scheme);
        await Assert.That(ticket.Identities.Length).IsEqualTo(AuthenticationExecutionProtocol.One);
        var identity = ticket.Identities.Single();
        await Assert.That(identity.IsAuthenticated).IsTrue();
        await Assert.That(identity.AuthenticationType).IsEqualTo(DatabaseAuthenticationComposition.Scheme);
        await Assert.That(identity.NameClaimType).IsEqualTo(ClaimsIdentity.DefaultNameClaimType);
        await Assert.That(identity.RoleClaimType).IsEqualTo(ClaimsIdentity.DefaultRoleClaimType);
        await Assert.That(identity.HasActor || identity.HasBootstrapContext || identity.HasLabel).IsFalse();
        await Assert.That(identity.Claims.Length).IsEqualTo(AuthenticationExecutionProtocol.One);
        var claim = identity.Claims.Single();
        await Assert.That(claim.Type).IsEqualTo(ClaimTypes.NameIdentifier);
        await Assert.That(claim.ValueType).IsEqualTo(ClaimValueTypes.String);
        await Assert.That(claim.Issuer).IsEqualTo(ClaimsIdentity.DefaultIssuer);
        await Assert.That(claim.OriginalIssuer).IsEqualTo(ClaimsIdentity.DefaultIssuer);
        await Assert.That(claim.PropertyCount).IsEqualTo(AuthenticationExecutionProtocol.NoEvents);
        await Assert.That(claim.MatchesSubject).IsTrue();
    }
}
