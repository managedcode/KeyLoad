using KeyLoad.Orleans;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAdmissionCases
{
    private const int SmallPayloadBytes = 1;
    private const string ClosedFailureDetail = "The operation was rejected safely.";

    internal static async Task AcCrs004AdmissionAcceptsOnlyTheTwoDefinedTerminalShapes(RequestCqrsClusterFixture fixture)
    {
        var requestId = Guid.NewGuid();
        var serializer = ChunkSerializer(fixture);
        var successful = new NativeCqrsStreamAdmission(serializer, requestId, fixture.RoutingOptions);
        successful.Admit(RequestCqrsProtocolCases.StartedChunk(requestId), CancellationToken.None);
        successful.Admit(RequestCqrsProtocolCases.CompletedChunk(new byte[SmallPayloadBytes]), CancellationToken.None);
        await Assert.That(successful.CompletionFailure(CancellationToken.None)).IsNull();

        var earlyFailure = new NativeCqrsStreamAdmission(serializer, requestId, fixture.RoutingOptions);
        earlyFailure.Admit(FailedChunk(ErrorCode.PermissionDenied, sequence: 1), CancellationToken.None);
        await Assert.That(earlyFailure.CompletionFailure(CancellationToken.None)).IsNull();
        var trailing = Assert.ThrowsExactly<KeyLoadException>(() =>
            earlyFailure.Admit(RequestCqrsProtocolCases.StartedChunk(requestId), CancellationToken.None));
        await Assert.That(trailing.Code).IsEqualTo(ErrorCode.OwnershipLost);

        var duplicateTerminal = new NativeCqrsStreamAdmission(serializer, requestId, fixture.RoutingOptions);
        duplicateTerminal.Admit(RequestCqrsProtocolCases.StartedChunk(requestId), CancellationToken.None);
        var completed = RequestCqrsProtocolCases.CompletedChunk(new byte[SmallPayloadBytes]);
        duplicateTerminal.Admit(completed, CancellationToken.None);
        var duplicate = Assert.ThrowsExactly<KeyLoadException>(() =>
            duplicateTerminal.Admit(completed, CancellationToken.None));
        await Assert.That(duplicate.Code).IsEqualTo(ErrorCode.OwnershipLost);
    }

    internal static async Task AcCrs004MalformedNativeChunkIsRejectedBeforeAdmission(RequestCqrsClusterFixture fixture, RequestCqrsInvalidChunk invalid)
    {
        var requestId = Guid.NewGuid();
        var serializer = ChunkSerializer(fixture);
        var admission = new NativeCqrsStreamAdmission(serializer, requestId, fixture.RoutingOptions);
        var chunk = InvalidChunk(invalid, requestId);
        if (invalid is not (RequestCqrsInvalidChunk.TerminalBeforeStarted
            or RequestCqrsInvalidChunk.WrongStartedIdentity))
        {
            admission.Admit(RequestCqrsProtocolCases.StartedChunk(requestId), CancellationToken.None);
        }

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => admission.Admit(chunk, CancellationToken.None));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
    }

    internal static async Task AcCrs004MissingTerminalAndCancellationCannotLookLikeCompletion(RequestCqrsClusterFixture fixture)
    {
        var requestId = Guid.NewGuid();
        var admission = new NativeCqrsStreamAdmission(ChunkSerializer(fixture), requestId, fixture.RoutingOptions);
        admission.Admit(RequestCqrsProtocolCases.StartedChunk(requestId), CancellationToken.None);
        var missingTerminal = admission.CompletionFailure(CancellationToken.None);
        await Assert.That(missingTerminal).IsTypeOf<KeyLoadException>();
        await Assert.That(((KeyLoadException)missingTerminal!).Code).IsEqualTo(ErrorCode.OwnershipLost);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancellation = admission.CompletionFailure(cancelled.Token);
        await Assert.That(cancellation).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)cancellation!).CancellationToken).IsEqualTo(cancelled.Token);
    }

    internal static async Task AcCrs004CompletedReplyPayloadRemainsInsideItsExistingRawReplyBound(RequestCqrsClusterFixture fixture)
    {
        var requestId = Guid.NewGuid();
        var admission = new NativeCqrsStreamAdmission(ChunkSerializer(fixture), requestId, fixture.RoutingOptions);
        admission.Admit(RequestCqrsProtocolCases.StartedChunk(requestId), CancellationToken.None);
        var overRawLimit = new byte[fixture.RoutingOptions.Value.MaximumReplyBytes + 1];
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            admission.Admit(RequestCqrsProtocolCases.CompletedChunk(overRawLimit), CancellationToken.None));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
    }

    private static Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ChunkSerializer(
        RequestCqrsClusterFixture fixture)
        => fixture.Cluster.ServiceProvider.GetRequiredService<
            Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();

    private static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> FailedChunk(ErrorCode code,
        long sequence)
    {
        var problem = GrainRequestStreamProblem.Create(code, ClosedFailureDetail, UnitRoutingOptions.Routing());
        return new(CqrsStreamChunkKind.Failed, null,
            Result<GrainOperationReply>.Fail(problem), null, null, null, sequence);
    }

    private static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> InvalidChunk(
        RequestCqrsInvalidChunk invalid, Guid requestId)
    {
        var successReply = new GrainOperationReply { Payload = new byte[SmallPayloadBytes] };
        var successResult = Result<GrainOperationReply>.Succeed(successReply);
        var problem = GrainRequestStreamProblem.Create(ErrorCode.PermissionDenied, ClosedFailureDetail, UnitRoutingOptions.Routing());
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

internal enum RequestCqrsInvalidChunk
{
    UnknownKind,
    WrongSequence,
    WrongEventType,
    ProgressKind,
    TerminalBeforeStarted,
    FailedWithSuccess,
    CompletedWithProblem,
    FailedWithValue,
    WrongStartedIdentity
}
