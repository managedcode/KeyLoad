using System.Net;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.ClientApi;

/// <summary>AC-MCP-003/005/007 and AC-ROUTE-001: execution response start safely publishes request identities.</summary>
internal sealed class OperationHeaderStartTests
{
    internal const int OperationTimeoutSeconds = 10;
    internal const string BeforeStartPath = "/before-start";
    internal const string NoExecutionPath = "/no-execution";
    internal const string StartedBeforeExecutionPath = "/start-before-execution";
    internal const string ParallelPathPrefix = "/parallel/";
    internal const string ResponseBody = "operation-response";

    /// <summary>An identity published before execution response start matches the caller-visible body and header.</summary>
    [Test]
    public async Task ExecutionIdentityBeforeStartCorrelatesHeaderAndBody()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var server = await OperationHeaderTestServer.StartAsync(cancellationToken);
        using var response = await server.GetAsync(BeforeStartPath, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var id = Guid.ParseExact(body, OrleansNodeProtocol.GuidFormat);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(id).IsNotEqualTo(Guid.Empty);
        await Assert.That(body).IsEqualTo(id.ToString(OrleansNodeProtocol.GuidFormat));
        await Assert.That((OperationHeaderTestServer.RequestIdHeader(response)).Value).IsEqualTo(body);
    }

    /// <summary>A response without a dispatched operation has no execution request ID header.</summary>
    [Test]
    public async Task ResponseWithoutExecutionHasNoRequestIdHeader()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var server = await OperationHeaderTestServer.StartAsync(cancellationToken);
        using var response = await server.GetAsync(NoExecutionPath, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsEqualTo(ResponseBody);
        await Assert.That((OperationHeaderTestServer.RequestIdHeader(response)).Value).IsNull();
    }

    /// <summary>Publishing after execution headers start preserves the response body and does not mutate committed headers.</summary>
    [Test]
    public async Task StartedResponseRemainsHealthyWhenIdentityArrivesLater()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var server = await OperationHeaderTestServer.StartAsync(cancellationToken);
        using var response = await server.GetAsync(StartedBeforeExecutionPath, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var id = Guid.ParseExact(body, OrleansNodeProtocol.GuidFormat);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(id).IsNotEqualTo(Guid.Empty);
        await Assert.That(body).IsEqualTo(id.ToString(OrleansNodeProtocol.GuidFormat));
        await Assert.That((OperationHeaderTestServer.RequestIdHeader(response)).Value).IsNull();
    }

    /// <summary>Overlapping execution requests each return only their own published ID in the body and header.</summary>
    [Test]
    public async Task ConcurrentResponsesKeepRequestIdentitiesIsolated()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var server = await OperationHeaderTestServer.StartAsync(cancellationToken, requireConcurrentRequests: true);
        var identities = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var responses = await Task.WhenAll(identities.Select(async id =>
        {
            using var response = await server.GetAsync(
                $"{ParallelPathPrefix}{id.ToString(OrleansNodeProtocol.GuidFormat)}", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (Expected: id, response.StatusCode, Header: (OperationHeaderTestServer.RequestIdHeader(response)).Value, Body: body);
        }));

        foreach (var result in responses)
        {
            var expected = result.Expected.ToString(OrleansNodeProtocol.GuidFormat);
            await Assert.That(result.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(result.Expected).IsNotEqualTo(Guid.Empty);
            await Assert.That(result.Header).IsEqualTo(expected);
            await Assert.That(result.Body).IsEqualTo(expected);
        }

        await Assert.That(responses[0].Header).IsNotEqualTo(responses[1].Header);
    }
}
