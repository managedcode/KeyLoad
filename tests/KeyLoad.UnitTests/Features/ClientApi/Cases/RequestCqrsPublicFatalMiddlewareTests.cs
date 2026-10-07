using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using ManagedCode.Communication.CQRS;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-CRS-003/004: the actual outer HTTP boundary preserves native fatal exceptions.</summary>
[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class RequestCqrsPublicFatalMiddlewareTests
{
    private const int DeepAggregateLayers = 256;
    private const string PrivateMessage = "private-fatal-message-marker";
    private const string PrivateDataKey = "private-fatal-data-key";
    private const string PrivateData = "private-fatal-data-marker";
    private const string StackMarker = nameof(ThrowPrivateStackMarker);
    private const string NoFatalObserved = "The middleware continuation did not propagate its configured fatal.";

    [Test]
    public async Task AcCrs003004HttpBoundaryPreservesNativeFatalIdentityAndAllowsNextRequest()
    {
        using var capture = new RequestFailureDiagnosticEventSourceCapture();
        using var factory = RequestFailureDiagnosticLoggerFactory.Create();
        var logger = factory.CreateLogger<ServerErrorMiddleware>();
        foreach (var type in new[] { typeof(OutOfMemoryException), typeof(StackOverflowException), typeof(AccessViolationException) })
        {
            await AssertDirectAndNestedFatalAsync(type, logger, nested: false);
            await AssertDirectAndNestedFatalAsync(type, logger, nested: true);
        }

        await Assert.That(capture.Text).IsEqualTo(string.Empty);
    }

    private static async Task AssertDirectAndNestedFatalAsync(Type type, ILogger<ServerErrorMiddleware> logger, bool nested)
    {
        var fatal = CreateFatal(type);
        var expected = fatal;
        if (nested)
        {
            for (var depth = 0; depth < DeepAggregateLayers; depth++)
            {
                expected = new AggregateException(expected);
            }
        }

        var attempts = 0;
        RequestDelegate continuation = context =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                ExceptionDispatchInfo.Capture(expected).Throw();
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        };
        var middleware = new ServerErrorMiddleware(continuation, logger);
        using var failedBody = new MemoryStream();
        var failedContext = CreateContext(failedBody);
        var escaped = await ObserveFatalAsync(middleware.InvokeAsync(failedContext));
        await Assert.That(ReferenceEquals(escaped, expected)).IsTrue();
        await Assert.That(ReferenceEquals(CqrsRuntimeFailures.FindFatal(escaped), fatal)).IsTrue();
        await Assert.That(fatal.Message).IsEqualTo(PrivateMessage);
        await Assert.That(fatal.Data[PrivateDataKey]).IsEqualTo(PrivateData);
        await Assert.That(fatal.StackTrace).Contains(StackMarker);
        await Assert.That(failedContext.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(failedContext.Response.Body.Length).IsEqualTo(0);

        using var healthyBody = new MemoryStream();
        var healthyContext = CreateContext(healthyBody);
        await middleware.InvokeAsync(healthyContext);
        await Assert.That(healthyContext.Response.StatusCode).IsEqualTo(StatusCodes.Status204NoContent);
        await Assert.That(attempts).IsEqualTo(2);
    }

    private static Exception CreateFatal(Type type)
    {
        var created = (Exception)Activator.CreateInstance(type, PrivateMessage)!;
        created.Data[PrivateDataKey] = PrivateData;
        return CaptureStack(created);
    }

    private static Exception CaptureStack(Exception error)
    {
        try
        {
            ThrowPrivateStackMarker(error);
        }
        catch (Exception caught) when (ReferenceEquals(caught, error))
        {
            return caught;
        }

        throw new InvalidOperationException(NoFatalObserved);
    }

    [DoesNotReturn]
    private static void ThrowPrivateStackMarker(Exception error) => ExceptionDispatchInfo.Capture(error).Throw();

    private static async Task<Exception> ObserveFatalAsync(Task operation)
    {
        try
        {
            await operation;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }

        throw new InvalidOperationException(NoFatalObserved);
    }

    private static DefaultHttpContext CreateContext(Stream body)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return context;
    }
}
