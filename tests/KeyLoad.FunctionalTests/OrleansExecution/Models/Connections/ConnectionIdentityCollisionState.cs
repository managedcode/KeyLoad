using System.Collections.Concurrent;
using KeyLoad.FunctionalTests.ClientApi;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed record ConnectionIdentityCollisionState(ConnectionScenario Scenario,
    PrincipalRecord First, PrincipalRecord Second, CommandRequest Original, CommandRequest Following, Guid OriginalRequest)
{
    internal ConcurrentDictionary<Guid, byte> IssuedRequests { get; } = new();
    internal DeclaredResponseEofTasks Tasks
    {
        get;
    } = new();
}
