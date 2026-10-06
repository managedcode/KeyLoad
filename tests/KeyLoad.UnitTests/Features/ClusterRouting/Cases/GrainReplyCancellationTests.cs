using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>AC-AISQL-013/AC-ROUTE-010: actor replies distinguish incoming cancellation from native interruption.</summary>
[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class GrainReplyCancellationTests
{
    private const string PrivateCanary = "private-cancellation-exception-canary";
    private const string CancelledDetail = "The database request was cancelled.";
    private const string UnavailableDetail = "The database request could not complete. Retry the same command ID for writes.";
    private const string CommandDetail = "The write outcome is unknown. Retry the same command ID.";
    private const string MissingCancellation = "The real framework task did not expose its cancellation exception.";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcRoute010ReadCancellationUsesTheActualIncomingToken(bool callerCancelled)
    {
        var error = await NativeCancellationAsync();
        using var incoming = new CancellationTokenSource();
        if (callerCancelled)
        { await incoming.CancelAsync(); }
        var expectedCode = callerCancelled ? ErrorCode.Cancelled : ErrorCode.OwnershipLost;
        var expectedDetail = callerCancelled ? CancelledDetail : UnavailableDetail;
        var requestId = Guid.NewGuid();
        var (reply, output) = FailureAndCapture(error, false, requestId, incoming.Token);

        await Assert.That(error.CancellationToken.IsCancellationRequested).IsTrue();
        await Assert.That(incoming.IsCancellationRequested).IsEqualTo(callerCancelled);
        await Assert.That(reply.Error).IsEqualTo(expectedCode);
        await Assert.That(reply.SafeDetail).IsEqualTo(expectedDetail);
        await Assert.That(reply.Payload.Length).IsEqualTo(0);
        await Assert.That(reply.SafeDetail).DoesNotContain(PrivateCanary);
        await VerifyDiagnosticsAsync(output, requestId, expectedCode, GrainFailureCategory.Cancellation);
    }

    [Test]
    public async Task AcRoute010MissingIncomingTokenCannotClaimCallerCancellation()
    {
        var error = await NativeCancellationAsync();
        var missing = GrainReplyFactory.Failure(error, false, null, UnitRoutingOptions.Routing());
        var inactive = GrainReplyFactory.Failure(error, false, null, UnitRoutingOptions.Routing(), cancellationToken: CancellationToken.None);

        await Assert.That(missing.Error).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(missing.SafeDetail).IsEqualTo(UnavailableDetail);
        await Assert.That(missing.Payload.Length).IsEqualTo(0);
        await Assert.That(inactive.Error).IsEqualTo(missing.Error);
        await Assert.That(inactive.SafeDetail).IsEqualTo(missing.SafeDetail);
        await Assert.That(inactive.Payload.Length).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcRoute010CommandInterruptionKeepsUnknownOutcomeForBothIncomingTokenStates(bool callerCancelled)
    {
        var error = await NativeCancellationAsync();
        using var incoming = new CancellationTokenSource();
        if (callerCancelled)
        { await incoming.CancelAsync(); }
        var requestId = Guid.NewGuid();
        var (reply, output) = FailureAndCapture(error, true, requestId, incoming.Token);

        await Assert.That(incoming.IsCancellationRequested).IsEqualTo(callerCancelled);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(reply.SafeDetail).IsEqualTo(CommandDetail);
        await Assert.That(reply.Payload.Length).IsEqualTo(0);
        await Assert.That(reply.SafeDetail).DoesNotContain(PrivateCanary);
        await VerifyDiagnosticsAsync(output, requestId, ErrorCode.UnknownWriteOutcome, GrainFailureCategory.Cancellation);
    }

    [Test]
    [Arguments(ErrorCode.RecoveryRequired, false)]
    [Arguments(ErrorCode.RecoveryRequired, true)]
    [Arguments(ErrorCode.PermissionDenied, false)]
    [Arguments(ErrorCode.PermissionDenied, true)]
    public async Task AcRoute010TypedDomainErrorsRemainAuthoritativeEvenWhenIncomingTokenIsCancelled(
        ErrorCode code, bool callerCancelled)
    {
        using var incoming = new CancellationTokenSource();
        if (callerCancelled)
        { await incoming.CancelAsync(); }
        var error = Errors.Fail(code, PrivateCanary);
        foreach (var command in new[] { false, true })
        {
            var requestId = Guid.NewGuid();
            var (reply, output) = FailureAndCapture(error, command, requestId, incoming.Token);
            await Assert.That(reply.Error).IsEqualTo(code);
            await Assert.That(reply.SafeDetail).IsEqualTo(error.Message);
            await Assert.That(reply.Payload.Length).IsEqualTo(0);
            await VerifyDiagnosticsAsync(output, requestId, code, GrainFailureCategory.Domain);
        }
        await Assert.That(error.Code).IsEqualTo(code);
        await Assert.That(error.Message).IsEqualTo(PrivateCanary);
    }

    private static async Task<OperationCanceledException> NativeCancellationAsync()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
        await cancellation.CancelAsync();
        var error = await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => pending)
            ?? throw new InvalidOperationException(MissingCancellation);
        error.Data[PrivateCanary] = PrivateCanary;
        return error;
    }

    private static (GrainOperationReply Reply, string Output) FailureAndCapture(Exception error, bool command,
        Guid requestId, CancellationToken cancellationToken)
    {
        using var capture = new EventSourceLogCapture();
        GrainOperationReply reply;
        using (var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger()))
        {
            reply = GrainReplyFactory.Failure(error, command, factory.CreateLogger(nameof(GrainFailureDiagnosticsTests)),
                UnitRoutingOptions.Routing(), requestId, GrainFailureStage.QuorumRead, cancellationToken);
        }
        return (reply, capture.Text);
    }

    private static async Task VerifyDiagnosticsAsync(string output, Guid requestId, ErrorCode code,
        GrainFailureCategory category)
    {
        await Assert.That(output).Contains(requestId.ToString());
        await Assert.That(output).Contains(nameof(GrainFailureStage.QuorumRead));
        await Assert.That(output).Contains(category.ToString());
        await Assert.That(output).Contains(code.ToString());
        await Assert.That(output).DoesNotContain(PrivateCanary);
        await Assert.That(output).DoesNotContain(typeof(TaskCanceledException).FullName!);
        await Assert.That(output).DoesNotContain(typeof(KeyLoadException).FullName!);
    }
}
