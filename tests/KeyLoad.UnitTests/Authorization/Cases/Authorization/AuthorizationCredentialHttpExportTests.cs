namespace KeyLoad.UnitTests.Features.Authorization;

[NotInParallel]
internal sealed class AuthorizationCredentialHttpExportTests
{
    [Test]
    public async Task RevokedNativeCredentialKeepsSafeProblemThenSameSecretRepairReplayAndLiteralHealthyExports()
    {
        using var fixture = new AuthorizationTelemetryHttpFixture(useServerErrorMiddleware: true);
        await fixture.RunAsync(AuthorizationCredentialHttpExportFlow.ExecuteAsync);
    }
}
