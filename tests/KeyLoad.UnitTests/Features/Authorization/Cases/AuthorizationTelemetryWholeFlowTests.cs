namespace KeyLoad.UnitTests.Features.Authorization;

[NotInParallel]
internal sealed class AuthorizationTelemetryWholeFlowTests
{
    [Test]
    public async Task AcAuthKl015002RealHttpAndLogExportsKeepSafeMetadataAfterDenialThenHealthyCatalog()
    {
        using var fixture = new AuthorizationTelemetryHttpFixture();
        await fixture.RunAsync(owner => AuthorizationTelemetryFlow.ExecuteAsync(owner, AuthorizationUnsafeSpanState.None));
    }

    [Test]
    [Arguments(AuthorizationUnsafeSpanState.Event)]
    [Arguments(AuthorizationUnsafeSpanState.Link)]
    [Arguments(AuthorizationUnsafeSpanState.ExcessTags)]
    [Arguments(AuthorizationUnsafeSpanState.ExcessBaggage)]
    [Arguments(AuthorizationUnsafeSpanState.InheritedParentBaggage)]
    [Arguments(AuthorizationUnsafeSpanState.ExcessParentLinks)]
    public async Task AcAuthKl015002NativeExporterSuppressesUnsafeServerStateAndExportsHealthyFollowup(AuthorizationUnsafeSpanState state)
    {
        using var fixture = new AuthorizationTelemetryHttpFixture();
        await fixture.RunAsync(owner => AuthorizationTelemetryFlow.ExecuteAsync(owner, state));
    }
}

[NotInParallel]
internal sealed class AuthorizationTelemetryClientWholeFlowTests
{
    [Test]
    [Arguments(AuthorizationUnsafeSpanState.ClientEvent)]
    [Arguments(AuthorizationUnsafeSpanState.ClientTaggedLink)]
    [Arguments(AuthorizationUnsafeSpanState.ClientTraceStateLink)]
    [Arguments(AuthorizationUnsafeSpanState.ClientExtraLink)]
    public async Task AcAuthKl015002UnsafeNativeClientStateSuppressesOnlyDenialAndExportsHealthyCatalog(AuthorizationUnsafeSpanState state)
    {
        using var fixture = new AuthorizationTelemetryHttpFixture();
        await fixture.RunAsync(owner => AuthorizationTelemetryFlow.ExecuteAsync(owner, state));
    }
}
