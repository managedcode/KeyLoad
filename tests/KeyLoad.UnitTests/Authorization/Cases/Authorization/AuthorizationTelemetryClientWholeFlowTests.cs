namespace KeyLoad.UnitTests.Features.Authorization;

[NotInParallel]
internal sealed class AuthorizationTelemetryClientWholeFlowTests
{
    [Test]
    [Arguments(AuthorizationUnsafeSpanState.ClientEvent)]
    [Arguments(AuthorizationUnsafeSpanState.ClientTaggedLink)]
    [Arguments(AuthorizationUnsafeSpanState.ClientTraceStateLink)]
    [Arguments(AuthorizationUnsafeSpanState.ClientExtraLink)]
    [Arguments(AuthorizationUnsafeSpanState.ClientRemoteLink)]
    public async Task AcAuthKl015002UnsafeNativeClientStateSuppressesOnlyDenialAndExportsHealthyCatalog(AuthorizationUnsafeSpanState state)
    {
        using var fixture = new AuthorizationTelemetryHttpFixture();
        await fixture.RunAsync(owner => AuthorizationTelemetryFlow.ExecuteAsync(owner, state,
            repairPersistedMember: state == AuthorizationUnsafeSpanState.ClientRemoteLink));
    }
}
