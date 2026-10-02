using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using Microsoft.Extensions.Logging;
using global::Orleans.Runtime;
using global::Orleans.Runtime.Messaging;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>AC-AISQL-011/AC-ROUTE-009: initial native RPC failures preserve outcome uncertainty and privacy.</summary>
[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class OrleansRpcFailureTests
{
    private const string PrivateCanary = "private-rpc-identity-envelope-endpoint-canary";
    private const string ReadDetail = "The Orleans request could not complete.";
    private const string CommandDetail = "The write outcome is unknown. Retry the same command ID.";
    private const string OrleansCategory = "Orleans";
    private const string TimeoutCategory = "Timeout";

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
            await Assert.That(OrleansRpcFailure.IsNative(error)).IsTrue();
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
    public async Task AcRoute009CancellationDomainRecoveryAndOtherFailuresRemainOutsideTheNativeCatch()
    {
        Exception[] excluded =
        [
            Errors.Fail(ErrorCode.RecoveryRequired, PrivateCanary),
            Errors.Fail(ErrorCode.PermissionDenied, PrivateCanary),
            new OperationCanceledException(PrivateCanary), new JsonException(PrivateCanary),
            new ArgumentException(PrivateCanary), new InvalidOperationException(PrivateCanary),
            new IOException(PrivateCanary)
        ];
        foreach (var error in excluded)
        {
            await Assert.That(OrleansRpcFailure.IsNative(error)).IsFalse();
        }
        await Assert.That(((KeyLoadException)excluded[0]).Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(excluded[0].Message).IsEqualTo(PrivateCanary);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcRoute009CallerCancellationWinsOverANativeFailureAndProducesNoFailureLog(bool command)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var capture = new EventSourceLogCapture();
        using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
        var logger = factory.CreateLogger(nameof(GrainFailureDiagnosticsTests));
        foreach (var (error, _) in NativeFailures())
        {
            var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() =>
                OrleansRpcFailure.Translate(error, command, Guid.NewGuid(), logger, cancellation.Token));
            await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
        }
        await Assert.That(capture.Text).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task AcRoute009TranslatorCannotAccidentallyConvertAStorageRecoveryError()
    {
        var original = Errors.Fail(ErrorCode.RecoveryRequired, PrivateCanary);
        using var capture = new EventSourceLogCapture();
        using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
        var logger = factory.CreateLogger(nameof(GrainFailureDiagnosticsTests));
        var rejected = Assert.ThrowsExactly<ArgumentException>(() =>
            OrleansRpcFailure.Translate(original, true, Guid.NewGuid(), logger, CancellationToken.None));
        await Assert.That(rejected.Message).DoesNotContain(PrivateCanary);
        await Assert.That(original.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(capture.Text).IsEqualTo(string.Empty);
    }

    private static IEnumerable<(Exception Error, string Category)> NativeFailures()
    {
        yield return (new OrleansException(PrivateCanary), OrleansCategory);
        yield return (new ConnectionFailedException(PrivateCanary, new IOException(PrivateCanary)), OrleansCategory);
        yield return (new TimeoutException(PrivateCanary, new IOException(PrivateCanary)), TimeoutCategory);
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
