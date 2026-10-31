using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsAdmissionCases
{
    private const int SmallPayloadBytes = 1;
    private const string ClosedFailureDetail = "The operation was rejected safely.";

    internal static async Task AcCrs004AdmissionAcceptsOnlyTheTwoDefinedTerminalShapes()
    {
        var requestId = Guid.NewGuid();
        var successful = new CqrsStreamAdmission(requestId);
        successful.Admit(RequestCqrsProtocolCases.StartedChunk(requestId));
        successful.Admit(RequestCqrsProtocolCases.CompletedChunk(new byte[SmallPayloadBytes]));
        await Assert.That((successful.CompletionFailure(CancellationToken.None)).Value).IsNull();

        var earlyFailure = new CqrsStreamAdmission(requestId);
        earlyFailure.Admit(FailedChunk(ErrorCode.PermissionDenied, sequence: 1));
        await Assert.That((earlyFailure.CompletionFailure(CancellationToken.None)).Value).IsNull();
        var trailing = Assert.ThrowsExactly<KeyLoadException>(() =>
            earlyFailure.Admit(RequestCqrsProtocolCases.StartedChunk(requestId)));
        await Assert.That(trailing.Code).IsEqualTo(ErrorCode.OwnershipLost);

        var duplicateTerminal = new CqrsStreamAdmission(requestId);
        duplicateTerminal.Admit(RequestCqrsProtocolCases.StartedChunk(requestId));
        var completed = RequestCqrsProtocolCases.CompletedChunk(new byte[SmallPayloadBytes]);
        duplicateTerminal.Admit(completed);
        var duplicate = Assert.ThrowsExactly<KeyLoadException>(() =>
            duplicateTerminal.Admit(completed));
        await Assert.That(duplicate.Code).IsEqualTo(ErrorCode.OwnershipLost);
    }

    internal static async Task AcCrs004MalformedChunkIsRejectedBeforeAdmission(RequestCqrsInvalidChunk invalid)
    {
        var requestId = Guid.NewGuid();
        var admission = new CqrsStreamAdmission(requestId);
        var chunk = InvalidChunk(invalid, requestId);
        if (invalid is not (RequestCqrsInvalidChunk.TerminalBeforeStarted
            or RequestCqrsInvalidChunk.WrongStartedIdentity))
        {
            admission.Admit(RequestCqrsProtocolCases.StartedChunk(requestId));
        }

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => admission.Admit(chunk));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
    }

    internal static async Task AcCrs004MissingTerminalAndCancellationCannotLookLikeCompletion()
    {
        var requestId = Guid.NewGuid();
        var admission = new CqrsStreamAdmission(requestId);
        admission.Admit(RequestCqrsProtocolCases.StartedChunk(requestId));
        var missingTerminal = (admission.CompletionFailure(CancellationToken.None)).Value;
        await Assert.That(missingTerminal).IsTypeOf<KeyLoadException>();
        await Assert.That(((KeyLoadException)missingTerminal!).Code).IsEqualTo(ErrorCode.OwnershipLost);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancellation = (admission.CompletionFailure(cancelled.Token)).Value;
        await Assert.That(cancellation).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)cancellation!).CancellationToken).IsEqualTo(cancelled.Token);
    }

    private static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> FailedChunk(ErrorCode code,
        long sequence)
    {
        var problem = GrainRequestStreamProblem.Create(code, ClosedFailureDetail);
        return new(CqrsStreamChunkKind.Failed, null,
            Result<GrainOperationReply>.Fail(problem), null, null, null, sequence);
    }

    private static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> InvalidChunk(
        RequestCqrsInvalidChunk invalid, Guid requestId)
    {
        var successReply = new GrainOperationReply { Result = new GrainValue(new byte[SmallPayloadBytes]) };
        var successResult = Result<GrainOperationReply>.Succeed(successReply);
        var problem = GrainRequestStreamProblem.Create(ErrorCode.PermissionDenied, ClosedFailureDetail);
        return invalid switch
        {
            RequestCqrsInvalidChunk.UnknownKind => new((CqrsStreamChunkKind)int.MaxValue,
                null, successResult, null, null, null, 2),
            RequestCqrsInvalidChunk.WrongSequence => new(CqrsStreamChunkKind.Completed,
                null, successResult, null, null, null, 3),
            RequestCqrsInvalidChunk.WrongEventType => new(CqrsStreamChunkKind.Completed,
                null, successResult, null, "private-event", null, 2),
            RequestCqrsInvalidChunk.ProgressKind => new(CqrsStreamChunkKind.Progress,
                Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(requestId)), null,
                null, null, null, 2),
            RequestCqrsInvalidChunk.TerminalBeforeStarted => RequestCqrsProtocolCases.CompletedChunk(new byte[SmallPayloadBytes]),
            RequestCqrsInvalidChunk.FailedWithSuccess => new(CqrsStreamChunkKind.Failed,
                null, successResult, null, null, null, 2),
            RequestCqrsInvalidChunk.CompletedWithProblem => new(CqrsStreamChunkKind.Completed,
                null, Result<GrainOperationReply>.Fail(problem), null, null, null, 2),
            RequestCqrsInvalidChunk.FailedWithValue => new(CqrsStreamChunkKind.Failed,
                null, new Result<GrainOperationReply> { Value = successReply }, null, null, null, 2),
            RequestCqrsInvalidChunk.WrongStartedIdentity => RequestCqrsProtocolCases.StartedChunk(Guid.NewGuid()),
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
    }
}
