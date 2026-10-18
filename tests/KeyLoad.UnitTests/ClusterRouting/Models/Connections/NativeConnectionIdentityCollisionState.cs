using System.Collections.Concurrent;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeConnectionIdentityCollisionState(ConnectionNativeScenario Scenario,
    PrincipalRecord First, PrincipalRecord Second, CommandRequest Original, CommandRequest Following, Guid OriginalRequest)
{
    internal ConcurrentDictionary<Guid, DateTimeOffset> OriginalExpiries { get; } = new();
    internal DeclaredResponseEofTasks Tasks
    {
        get;
    } = new();
}
