using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class NativeCredentialStatusState(ConnectionNativeScenario scenario,
    PrincipalRecord administrator, PrincipalRecord ordinary, ApiKeyRecord caller,
    ApiKeyRecord original, ApiKeyRecord denied, ApiKeyRecord healthy)
{
    internal ConnectionNativeScenario Scenario
    {
        get;
    } = scenario;
    internal PrincipalRecord Administrator
    {
        get;
    } = administrator;
    internal PrincipalRecord Ordinary
    {
        get;
    } = ordinary;
    internal ApiKeyRecord Caller
    {
        get;
    } = caller;
    internal ApiKeyRecord Original
    {
        get;
    } = original;
    internal ApiKeyRecord Denied
    {
        get;
    } = denied;
    internal ApiKeyRecord Healthy
    {
        get;
    } = healthy;
    internal Guid OriginalId
    {
        get;
    } = Guid.NewGuid();
    internal Guid DeniedId
    {
        get;
    } = Guid.NewGuid();
    internal Guid HealthyId
    {
        get;
    } = Guid.NewGuid();
    internal DeclaredResponseEofTasks Tasks
    {
        get;
    } = new();
}
