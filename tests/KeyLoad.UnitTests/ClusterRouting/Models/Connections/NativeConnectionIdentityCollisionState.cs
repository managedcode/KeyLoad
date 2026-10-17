using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeConnectionIdentityCollisionState(ConnectionNativeScenario Scenario,
    PrincipalRecord First, PrincipalRecord Second, CommandRequest Original, CommandRequest Following, Guid OriginalRequest)
{
    internal DeclaredResponseEofTasks Tasks
    {
        get;
    } = new();
}
