namespace KeyLoad.UnitTests.Authorization;

[NotInParallel]
internal sealed class AuthorizationExpiredCredentialHttpExportTests
{
    [Test]
    public async Task ExpiredCredentialKeepsSafeProblemThenRepairReplayExportsAndSameRootColdHealthy()
    {
        using var fixture = new AuthorizationTelemetryHttpFixture(useServerErrorMiddleware: true);
        await fixture.RunAsync(AuthorizationCredentialHttpExportFlow.ExecuteExpiredAsync);
    }
}
