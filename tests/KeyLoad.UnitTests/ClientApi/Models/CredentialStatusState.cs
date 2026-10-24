using KeyLoad.UnitTests.OrleansExecution;

namespace KeyLoad.UnitTests.ClientApi;

internal sealed class CredentialStatusState(ConnectionScenario scenario,
    PrincipalRecord administrator, PrincipalRecord ordinary, ApiKeyRecord caller,
    ApiKeyRecord original, ApiKeyRecord denied, ApiKeyRecord healthy)
{
    internal ConnectionScenario Scenario
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
