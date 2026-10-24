namespace KeyLoad.UnitTests.Authorization;

[NotInParallel]
internal sealed class AuthorizationMalformedHttpExportTests
{
    [Test]
    public async Task MalformedHttpBodyKeepsSafeProblemThenSameMemberRepairReplayAndLiteralHealthyExports()
    {
        using var fixture = new AuthorizationTelemetryHttpFixture(useServerErrorMiddleware: true);
        await fixture.RunAsync(AuthorizationMalformedHttpExportFlow.ExecuteAsync);
    }
}
