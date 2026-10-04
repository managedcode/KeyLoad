using System.Text.Json;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>AC-CRS-005 and AC-ROUTE-009: interrupted request streams preserve outcome uncertainty and privacy.</summary>
[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class OrleansRpcFailureTests
{
    private const string PrivateCanary = "private-rpc-identity-envelope-endpoint-canary";
    private const string ReadDetail = "The Orleans request could not complete.";
    private const string CommandDetail = "The write outcome is unknown. Retry the same command ID.";
    private const string OrleansCategory = "Orleans";
    private const string TimeoutCategory = "Timeout";
    private const string CancellationCategory = "Cancellation";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcRoute009NativeFrameworkFailuresUseFixedReadOrCommandOutcomes(bool command)
    {
        var expectedCode = command ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost;
        var expectedDetail = command ? CommandDetail : ReadDetail;
        foreach (var (error, category) in NativeFailures())
        {
            var requestId = Guid.NewGuid();
            var (failure, output) = TranslateAndCapture(error, command, requestId);
            await Assert.That(NativeCqrsBoundaryErrors.IsNonFatal(error)).IsTrue();
            await Assert.That(failure.Code).IsEqualTo(expectedCode);
            await Assert.That(failure.Message).IsEqualTo(expectedDetail);
            await Assert.That(failure.InnerException).IsNull();
            await Assert.That(output).Contains(requestId.ToString());
            await Assert.That(output).Contains(category);
            await Assert.That(output).Contains(expectedCode.ToString());
            await Assert.That(output).DoesNotContain(PrivateCanary);
            await Assert.That(output).DoesNotContain(error.GetType().FullName!);
            await Assert.That(failure.Message).DoesNotContain(PrivateCanary);
        }
    }

    [Test]
    public async Task AcCrs005UnvalidatedStreamFailuresDoNotExposeArbitraryExceptionDetails()
    {
        Exception[] failures =
        [
            Errors.Fail(ErrorCode.RecoveryRequired, PrivateCanary),
            Errors.Fail(ErrorCode.PermissionDenied, PrivateCanary),
            new JsonException(PrivateCanary),
            new ArgumentException(PrivateCanary), new InvalidOperationException(PrivateCanary),
            new IOException(PrivateCanary)
        ];
        foreach (var error in failures)
        {
            var (failure, output) = TranslateAndCapture(error, true, Guid.NewGuid());
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await Assert.That(failure.Message).IsEqualTo(CommandDetail);
            await Assert.That(failure.InnerException).IsNull();
            await Assert.That(output).DoesNotContain(PrivateCanary);
            await Assert.That(output).DoesNotContain(error.GetType().FullName!);
        }
        await Assert.That(((KeyLoadException)failures[0]).Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(failures[0].Message).IsEqualTo(PrivateCanary);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcCrs005CallerCancellationPreservesWriteUncertaintyAndDistinctReadCancellation(bool command)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var capture = new EventSourceLogCapture();
        using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
        var logger = factory.CreateLogger(nameof(GrainFailureDiagnosticsTests));
        foreach (var (error, _) in NativeFailures())
        {
            var cancelled = OrleansRpcFailure.Translate(error, command, Guid.NewGuid(), logger, cancellation.Token);
            await Assert.That(cancelled.Code).IsEqualTo(command ? ErrorCode.UnknownWriteOutcome : ErrorCode.Cancelled);
            await Assert.That(cancelled.InnerException).IsNull();
        }
        if (command)
        {
            await Assert.That(capture.Text).Contains(ErrorCode.UnknownWriteOutcome.ToString());
            await Assert.That(capture.Text).DoesNotContain(PrivateCanary);
        }
        else
        {
            await Assert.That(capture.Text).IsEqualTo(string.Empty);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcRoute009ForeignCancellationKeepsReadOrCommandOutcomeWithAnActiveCaller(bool command)
    {
        using var foreignCancellation = new CancellationTokenSource();
        await foreignCancellation.CancelAsync();
        var original = new OperationCanceledException(PrivateCanary, foreignCancellation.Token);
        var requestId = Guid.NewGuid();
        var (failure, output) = TranslateAndCapture(original, command, requestId);
        var expectedCode = command ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost;

        await Assert.That(NativeCqrsBoundaryErrors.IsNonFatal(original)).IsTrue();
        await Assert.That(original.CancellationToken).IsEqualTo(foreignCancellation.Token);
        await Assert.That(failure.Code).IsEqualTo(expectedCode);
        await Assert.That(failure.Message).IsEqualTo(command ? CommandDetail : ReadDetail);
        await Assert.That(failure.InnerException).IsNull();
        await Assert.That(output).Contains(requestId.ToString());
        await Assert.That(output).Contains(CancellationCategory);
        await Assert.That(output).Contains(expectedCode.ToString());
        await Assert.That(output).DoesNotContain(PrivateCanary);
        await Assert.That(output).DoesNotContain(original.GetType().FullName!);
    }

    [Test]
    public async Task AcCrs006TranslatorRejectsFatalRuntimeFailuresWithoutConversionOrLogging()
    {
        Type[] fatalTypes = [typeof(OutOfMemoryException), typeof(StackOverflowException), typeof(AccessViolationException)];
        using var capture = new EventSourceLogCapture();
        using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
        var logger = factory.CreateLogger(nameof(GrainFailureDiagnosticsTests));
        foreach (var fatalType in fatalTypes)
        {
            var original = (Exception)Activator.CreateInstance(fatalType, PrivateCanary)!;
            await Assert.That(original.GetType()).IsEqualTo(fatalType);
            await Assert.That(original.Message).IsEqualTo(PrivateCanary);
            var rejected = Assert.ThrowsExactly<ArgumentException>(() =>
                OrleansRpcFailure.Translate(original, true, Guid.NewGuid(), logger, CancellationToken.None));
            await Assert.That(rejected.Message).DoesNotContain(PrivateCanary);
            await Assert.That(NativeCqrsBoundaryErrors.IsNonFatal(original)).IsFalse();
        }
        await Assert.That(capture.Text).IsEqualTo(string.Empty);
    }

    private static IEnumerable<(Exception Error, string Category)> NativeFailures()
    {
        yield return (new OrleansException(PrivateCanary), OrleansCategory);
        yield return (new global::Orleans.Runtime.Messaging.ConnectionFailedException(PrivateCanary, new IOException(PrivateCanary)), OrleansCategory);
        yield return (new TimeoutException(PrivateCanary, new IOException(PrivateCanary)), TimeoutCategory);
        yield return (new OperationCanceledException(PrivateCanary), CancellationCategory);
        yield return (new TaskCanceledException(PrivateCanary), CancellationCategory);
    }

    private static (KeyLoadException Failure, string Output) TranslateAndCapture(Exception error, bool command, Guid requestId)
    {
        using var capture = new EventSourceLogCapture();
        KeyLoadException failure;
        using (var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger()))
        {
            var logger = factory.CreateLogger(nameof(GrainFailureDiagnosticsTests));
            failure = OrleansRpcFailure.Translate(error, command, requestId, logger, CancellationToken.None);
        }
        return (failure, capture.Text);
    }
}
