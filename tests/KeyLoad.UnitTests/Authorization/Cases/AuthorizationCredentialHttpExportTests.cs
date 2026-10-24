namespace KeyLoad.UnitTests.Authorization;

[NotInParallel]
internal sealed class AuthorizationCredentialHttpExportTests
{
    [Test]
    public async Task RevokedCredentialKeepsSafeProblemThenSameSecretRepairReplayAndLiteralHealthyExports()
    {
        using var fixture = new AuthorizationTelemetryHttpFixture(useServerErrorMiddleware: true);
        await fixture.RunAsync(AuthorizationCredentialHttpExportFlow.ExecuteAsync);
    }
}
