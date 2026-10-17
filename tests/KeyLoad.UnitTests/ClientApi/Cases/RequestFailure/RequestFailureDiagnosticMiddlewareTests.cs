using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Text.Json;
using System.Text.RegularExpressions;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventSource;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-DIAG-001/002: middleware diagnostics retain only closed dispatch evidence.</summary>
[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class RequestFailureDiagnosticMiddlewareTests
{
    private const string ExceptionMessageCanary = "private-message-canary";
    private const string CredentialCanary = "private-credential-canary";
    private const string ExpectedGenericDetail = "The server could not complete the database operation.";
    private const string ErrorCodeProperty = "errorCode";
    private const string DetailProperty = "detail";

    [Test]
    public async Task CredentialFailureKeepsGeneric503AndLogsNoOperationIdentityOrPrivateText()
    {
        var context = CreateContext();
        RequestFailureDiagnostic.MarkCredentialDispatch(context);
        var (status, body, output) = await InvokeFailureAsync(context,
            new PrivateTypeCanaryException(ExceptionMessageCanary, CredentialCanary));

        await AssertGenericResponseAsync(status, body);
        await Assert.That(output).Contains(nameof(RequestFailurePhase.CredentialDispatch));
        await Assert.That(output).Contains(nameof(RequestFailureCategory.Other));
        await Assert.That(Regex.IsMatch(output,
            @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b",
            RegexOptions.IgnoreCase)).IsFalse();
        await AssertPrivateValuesAbsentAsync(output);
    }

    [Test]
    public async Task CommandFailureLogsExistingOperationGuidAndClosedFailureCategory()
    {
        var context = CreateContext();
        var operationId = Guid.NewGuid();
        OperationResponseHeaders.Register(context);
        OperationResponseHeaders.Publish(context, operationId);
        RequestFailureDiagnostic.MarkOperationDispatch(context, isCommand: true);

        var (status, body, output) = await InvokeFailureAsync(context,
            new PrivateTypeCanaryException(ExceptionMessageCanary, CredentialCanary));

        await AssertGenericResponseAsync(status, body);
        await Assert.That(output).Contains(nameof(RequestFailurePhase.CommandDispatch));
        await Assert.That(output).Contains(nameof(RequestFailureCategory.Other));
        await Assert.That(output).Contains(operationId.ToString());
        await AssertUtcTimestampAsync(output);
        await AssertPrivateValuesAbsentAsync(output);
    }

    [Test]
    public async Task ReadFailureLogsReadPhaseAndExistingOperationGuid()
    {
        var context = CreateContext();
        var operationId = Guid.NewGuid();
        OperationResponseHeaders.Register(context);
        OperationResponseHeaders.Publish(context, operationId);
        RequestFailureDiagnostic.MarkOperationDispatch(context, isCommand: false);

        var (status, body, output) = await InvokeFailureAsync(context,
            new PrivateTypeCanaryException(ExceptionMessageCanary, CredentialCanary));

        await AssertGenericResponseAsync(status, body);
        await Assert.That(output).Contains(nameof(RequestFailurePhase.ReadDispatch));
        await Assert.That(output).Contains(operationId.ToString());
        await AssertUtcTimestampAsync(output);
        await AssertPrivateValuesAbsentAsync(output);
    }

    [Test]
    public async Task UnmarkedTimeoutUsesNonePhaseAndKnownClosedCategory()
    {
        var context = CreateContext();
        var (status, body, output) = await InvokeFailureAsync(context, new TimeoutException(ExceptionMessageCanary));

        await AssertGenericResponseAsync(status, body);
        await Assert.That(output).Contains(nameof(RequestFailurePhase.None));
        await Assert.That(output).Contains(nameof(RequestFailureCategory.Timeout));
        await Assert.That(output).DoesNotContain(ExceptionMessageCanary);
        await AssertUtcTimestampAsync(output);
    }

    [Test]
    public async Task InvalidOperationFailureUsesClosedCategoryAndOmitsClrTypeAndMessage()
    {
        var context = CreateContext();
        var (status, body, output) = await InvokeFailureAsync(context,
            new InvalidOperationException(ExceptionMessageCanary));

        await AssertGenericResponseAsync(status, body);
        await Assert.That(output).Contains(nameof(RequestFailureCategory.InvalidOperation));
        await Assert.That(output).DoesNotContain(nameof(InvalidOperationException));
        await Assert.That(output).DoesNotContain(ExceptionMessageCanary);
        await AssertUtcTimestampAsync(output);
    }

    [Test]
    public async Task NativeOrleansFailureUsesClosedCategoryAndOmitsClrTypeAndMessage()
    {
        var context = CreateContext();
        var (status, body, output) = await InvokeFailureAsync(context, new OrleansException(ExceptionMessageCanary));

        await AssertGenericResponseAsync(status, body);
        await Assert.That(output).Contains(nameof(RequestFailureCategory.Orleans));
        await Assert.That(output).DoesNotContain(nameof(OrleansException));
        await Assert.That(output).DoesNotContain(ExceptionMessageCanary);
        await AssertUtcTimestampAsync(output);
    }

    [Test]
    public async Task MarkerValuesAreSharedClosedEnumsAndSuccessHasNoDiagnosticAllocation()
    {
        var first = CreateContext();
        var second = CreateContext();
        RequestFailureDiagnostic.MarkOperationDispatch(first, isCommand: true);
        RequestFailureDiagnostic.MarkOperationDispatch(second, isCommand: true);

        await Assert.That(first.Items.Count).IsEqualTo(1);
        await Assert.That(second.Items.Count).IsEqualTo(1);
        var firstMarker = first.Items.Single().Value;
        var secondMarker = second.Items.Single().Value;
        await Assert.That(firstMarker!.GetType().IsEnum).IsTrue();
        await Assert.That(ReferenceEquals(firstMarker, secondMarker)).IsTrue();
        await Assert.That(CreateContext().Items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DomainErrorKeepsItsExistingCodeAndDetailWithoutUnexpectedFailureLog()
    {
        const string domainDetail = "The database request is not authorized.";
        var context = CreateContext();
        using var capture = new RequestFailureDiagnosticEventSourceCapture();
        using var factory = CreateLoggerFactory();
        var middleware = new ServerErrorMiddleware(_ => throw Errors.Fail(ErrorCode.PermissionDenied, domainDetail),
            factory.CreateLogger<ServerErrorMiddleware>());

        await middleware.InvokeAsync(context);
        using var document = JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray());

        await Assert.That(context.Response.StatusCode).IsEqualTo(Errors.Status(ErrorCode.PermissionDenied));
        await Assert.That(document.RootElement.GetProperty(ErrorCodeProperty).GetString()).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await Assert.That(document.RootElement.GetProperty(DetailProperty).GetString()).IsEqualTo(domainDetail);
        await Assert.That(capture.Text).DoesNotContain("Database request failed");
    }

    [Test]
    public async Task CallerCancellationDoesNotBecomeGeneric503OrFailureLog()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var context = CreateContext(cancellation.Token);
        using var capture = new RequestFailureDiagnosticEventSourceCapture();
        using var factory = CreateLoggerFactory();
        var middleware = new ServerErrorMiddleware(_ => throw new OperationCanceledException(),
            factory.CreateLogger<ServerErrorMiddleware>());

        await middleware.InvokeAsync(context);

        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(context.Response.Body.Length).IsEqualTo(0);
        await Assert.That(capture.Text).DoesNotContain("Database request failed");
    }

    private static DefaultHttpContext CreateContext(CancellationToken requestAborted = default)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestAborted = requestAborted;
        return context;
    }

    private static async Task<(int Status, byte[] Body, string Output)> InvokeFailureAsync(HttpContext context, Exception error)
    {
        using var capture = new RequestFailureDiagnosticEventSourceCapture();
        using var factory = CreateLoggerFactory();
        var middleware = new ServerErrorMiddleware(_ => throw error,
            factory.CreateLogger<ServerErrorMiddleware>());
        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, ((MemoryStream)context.Response.Body).ToArray(), capture.Text);
    }

    private static ILoggerFactory CreateLoggerFactory()
        => RequestFailureDiagnosticLoggerFactory.Create();

    private static async Task AssertGenericResponseAsync(int status, byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        await Assert.That(status).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(document.RootElement.GetProperty(ErrorCodeProperty).GetString()).IsEqualTo(nameof(ErrorCode.RecoveryRequired));
        await Assert.That(document.RootElement.GetProperty(DetailProperty).GetString()).IsEqualTo(ExpectedGenericDetail);
    }

    private static async Task AssertPrivateValuesAbsentAsync(string output)
    {
        await Assert.That(output).DoesNotContain(ExceptionMessageCanary);
        await Assert.That(output).DoesNotContain(CredentialCanary);
        await Assert.That(output).DoesNotContain(nameof(PrivateTypeCanaryException));
    }

    private static async Task AssertUtcTimestampAsync(string output)
    {
        var timestampPattern = @"\b20\d\d-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}(?:\+00:00|Z)\b";
        await Assert.That(Regex.IsMatch(output, timestampPattern)).IsTrue();
    }

    private sealed class PrivateTypeCanaryException : Exception
    {
        public PrivateTypeCanaryException()
        {
        }

        public PrivateTypeCanaryException(string? message) : base(message)
        {
        }

        public PrivateTypeCanaryException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        public PrivateTypeCanaryException(string messageCanary, string credentialCanary)
            : base($"{messageCanary}; {credentialCanary}")
        {
        }
    }
}

/// <summary>Observes actual Microsoft logging EventSource formatted events for request failure diagnostics.</summary>
internal sealed class RequestFailureDiagnosticEventSourceCapture : EventListener
{
    private const string FilterSpecsKey = "FilterSpecs";
    private const string ServerErrorLoggerCategory = "KeyLoad.Server.ServerErrorMiddleware";
    private const string LoggerFilter = ServerErrorLoggerCategory + ":Error";
    private const int LoggerNamePayloadIndex = 2;
    private readonly ConcurrentQueue<string> messages = new();

    internal string Text => string.Join(" ", messages);

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "Microsoft-Extensions-Logging")
        {
            var arguments = new Dictionary<string, string?> { [FilterSpecsKey] = LoggerFilter };
            EnableEvents(eventSource, EventLevel.Error, LoggingEventSource.Keywords.FormattedMessage, arguments);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName == "FormattedMessage" && eventData.Payload is { } payload
            && payload.Count > LoggerNamePayloadIndex
            && payload[LoggerNamePayloadIndex] is string category
            && string.Equals(category, ServerErrorLoggerCategory, StringComparison.Ordinal))
        {
            messages.Enqueue(string.Join(" ", payload));
        }
    }
}
