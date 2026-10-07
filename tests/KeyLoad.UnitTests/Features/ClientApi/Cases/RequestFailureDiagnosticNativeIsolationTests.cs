using System.Text.Json;
using System.Text.RegularExpressions;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Actual native listener reconfiguration cannot revoke or contaminate the owned diagnostic.</summary>
[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class RequestFailureDiagnosticNativeIsolationTests
{
    private const string PrivateMessage = "private-failure-message-canary";
    private const string ForeignMessage = "private-other-capture-canary";
    private const string ForeignCategory = "McpTransportDiagnosticsTests";
    private const int ForeignEventId = 47;
    private const string ErrorCodeProperty = "errorCode";
    private const string DetailProperty = "detail";
    private const string SafeDetail = "The server could not complete the database operation.";
    private static readonly Action<ILogger, Exception?> WriteForeign = LoggerMessage.Define(
        LogLevel.Warning, new EventId(ForeignEventId), ForeignMessage);

    [Test]
    public async Task NativeListenerReconfigurationAndDisposalPreservePrivateSafeFailureAndHealthyRequest()
    {
        using var capture = new RequestFailureDiagnosticEventSourceCapture();
        using var factory = RequestFailureDiagnosticLoggerFactory.Create();
        var logger = factory.CreateLogger<ServerErrorMiddleware>();
        using (var other = new McpTransportEventSourceCapture())
        {
            using var otherFactory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
            WriteForeign(otherFactory.CreateLogger(ForeignCategory), null);
            await Assert.That(other.Text).Contains(ForeignMessage);
            await Assert.That(capture.Text).IsEqualTo(string.Empty);
            await Assert.That(logger.IsEnabled(LogLevel.Error)).IsTrue();
        }
        await Assert.That(logger.IsEnabled(LogLevel.Error)).IsTrue();
        var context = new DefaultHttpContext();
        using var response = new MemoryStream();
        context.Response.Body = response;
        var middleware = new ServerErrorMiddleware(_ => throw new InvalidOperationException(PrivateMessage), logger);
        await middleware.InvokeAsync(context);
        using var body = JsonDocument.Parse(response.ToArray());
        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(body.RootElement.GetProperty(ErrorCodeProperty).GetString()).IsEqualTo(nameof(ErrorCode.RecoveryRequired));
        await Assert.That(body.RootElement.GetProperty(DetailProperty).GetString()).IsEqualTo(SafeDetail);
        var failureOutput = capture.Text;
        await Assert.That(failureOutput).Contains(nameof(RequestFailureCategory.InvalidOperation));
        await Assert.That(failureOutput).Contains(nameof(RequestFailurePhase.None));
        await Assert.That(failureOutput).DoesNotContain(nameof(InvalidOperationException));
        await Assert.That(failureOutput).DoesNotContain(PrivateMessage);
        await Assert.That(failureOutput).DoesNotContain(ForeignMessage);
        await Assert.That(Regex.IsMatch(failureOutput, @"\b20\d\d-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}(?:\+00:00|Z)\b")).IsTrue();
        var healthy = new DefaultHttpContext();
        await new ServerErrorMiddleware(_ => Task.CompletedTask, logger).InvokeAsync(healthy);
        await Assert.That(healthy.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(capture.Text).IsEqualTo(failureOutput);
    }
}
