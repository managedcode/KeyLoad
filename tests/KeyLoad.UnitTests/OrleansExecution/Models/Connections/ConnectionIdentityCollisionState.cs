using System.Collections.Concurrent;
using KeyLoad.UnitTests.ClientApi;

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed record ConnectionIdentityCollisionState(ConnectionScenario Scenario,
    PrincipalRecord First, PrincipalRecord Second, CommandRequest Original, CommandRequest Following, Guid OriginalRequest)
{
    internal ConcurrentDictionary<Guid, DateTimeOffset> OriginalExpiries { get; } = new();
    internal DeclaredResponseEofTasks Tasks
    {
        get;
    } = new();
}
