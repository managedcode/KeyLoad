namespace KeyLoad.UnitTests.Features.Authorization;

[NotInParallel]
internal sealed class AuthorizationExpiredCredentialHttpExportTests
{
    [Test]
    public async Task ExpiredNativeCredentialKeepsSafeProblemThenRepairReplayExportsAndSameRootColdHealthy()
    {
        using var fixture = new AuthorizationTelemetryHttpFixture(useServerErrorMiddleware: true);
        await fixture.RunAsync(AuthorizationCredentialHttpExportFlow.ExecuteExpiredAsync);
    }
}
