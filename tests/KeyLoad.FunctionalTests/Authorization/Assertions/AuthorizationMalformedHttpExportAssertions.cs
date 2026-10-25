using System.Net;
using System.Text.Json;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class AuthorizationMalformedHttpExportAssertions
{
    internal static async Task ProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var actual = JsonDocument.Parse(body);
        using var expected = JsonDocument.Parse(AuthorizationMalformedHttpExportProtocol.ExpectedProblem);
        await Assert.That(JsonElement.DeepEquals(actual.RootElement, expected.RootElement)).IsTrue();
        foreach (var canary in AuthorizationTelemetryTestProtocol.Canaries)
        {
            await Assert.That(body.Contains(canary, StringComparison.Ordinal)).IsFalse();
        }
    }
}
