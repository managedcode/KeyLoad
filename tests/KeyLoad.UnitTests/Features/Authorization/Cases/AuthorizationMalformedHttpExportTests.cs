namespace KeyLoad.UnitTests.Features.Authorization;

[NotInParallel]
internal sealed class AuthorizationMalformedHttpExportTests
{
    [Test]
    public async Task MalformedNativeHttpBodyKeepsSafeProblemThenSameMemberRepairReplayAndLiteralHealthyExports()
    {
        using var fixture = new AuthorizationTelemetryHttpFixture(useServerErrorMiddleware: true);
        await fixture.RunAsync(AuthorizationMalformedHttpExportFlow.ExecuteAsync);
    }
}
