using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class RequestCqrsCommandStream
{
    private const int StartedSequence = 1;
    private const int CompletedSequence = 2;
    private const int EarlyFailedSequence = 1;
    private const int PostStartFailedSequence = 2;
    private const string MissingPreStreamRefusal = "The request stream was not refused before its first execution chunk.";

    internal static Task<GrainOperationReply> InvokeAsync(RequestCqrsHostedRuntimeFixture fixture, PrincipalRecord principal,
        Guid requestId, Guid commandId, CommandRequest command, Func<Task>? beforeFirstPull = null)
    {
        var signed = fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, commandId,
            OrleansSerialization.Serialize(command));
        return InvokeAsync(fixture, signed, principal, requestId, commandId, CompletedSequence,
            beforeFirstPull: beforeFirstPull);
    }

    internal static Task<GrainOperationReply> InvokeAsync(RequestCqrsHostedRuntimeFixture fixture, string signed,
        PrincipalRecord contextPrincipal, Guid requestId, Guid commandId, int terminalSequence,
        ErrorCode? expectedError = null, Func<Task>? beforeFirstPull = null)
        => InvokeCoreAsync(fixture, signed, contextPrincipal, requestId, commandId, terminalSequence,
            expectedError, beforeFirstPull);

    internal static async Task AssertPreStreamRefusalAsync(RequestCqrsHostedRuntimeFixture fixture,
        string signed, PrincipalRecord? contextPrincipal, Guid requestId, Guid commandId, ErrorCode expectedError)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRoutingTests.InvocationBound, TimeProvider.System);
        using IDisposable context = contextPrincipal is null
            ? RequestCqrsClientContext.Set(null, false, null, includeState: false)
            : new GrainRequestIdentityScope(fixture.Services, contextPrincipal,
                requestId, commandId, deadline.Token, connectionId: fixture.ConnectionId);
        var actor = fixture.Grains.GetGrain<IConnectionGrain>(fixture.ConnectionId);
        await using var iterator = actor.ExecuteStreamAsync(signed, deadline.Token)
            .WithBatchSize(GrainRequestStreamProtocol.BatchSize).GetAsyncEnumerator(deadline.Token);
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
        {
            _ = await iterator.MoveNextAsync();
        }) ?? throw new InvalidOperationException(MissingPreStreamRefusal);
        await Assert.That(failure.Code).IsEqualTo(expectedError);
    }

    private static async Task<GrainOperationReply> InvokeCoreAsync(RequestCqrsHostedRuntimeFixture fixture, string signed,
        PrincipalRecord contextPrincipal, Guid requestId, Guid commandId, int terminalSequence,
        ErrorCode? expectedError, Func<Task>? beforeFirstPull = null)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRoutingTests.InvocationBound, TimeProvider.System);
        using var context = new GrainRequestIdentityScope(fixture.Services, contextPrincipal,
            requestId, commandId, deadline.Token, connectionId: fixture.ConnectionId);
        var actor = fixture.Grains.GetGrain<IConnectionGrain>(fixture.ConnectionId);
        var stream = actor.ExecuteStreamAsync(signed, deadline.Token)
            .WithBatchSize(GrainRequestStreamProtocol.BatchSize);
        if (beforeFirstPull is not null)
        {
            await beforeFirstPull();
        }

        await using var iterator = stream.GetAsyncEnumerator(deadline.Token);
        if (!await iterator.MoveNextAsync())
        {
            throw new InvalidOperationException("The request stream ended before its first execution chunk.");
        }

        var first = iterator.Current;
        if (terminalSequence == EarlyFailedSequence)
        {
            await AssertFailedAsync(first, EarlyFailedSequence, expectedError!.Value);
            await Assert.That(await iterator.MoveNextAsync()).IsFalse();
            return FailedReply(first);
        }

        await AssertStartedAsync(first, requestId);
        if (!await iterator.MoveNextAsync())
        {
            throw new InvalidOperationException("The request stream ended before its terminal execution chunk.");
        }

        var terminal = iterator.Current;
        if (expectedError is { } error)
        {
            await AssertFailedAsync(terminal, PostStartFailedSequence, error);
        }
        else
        {
            await AssertCompletedAsync(terminal, CompletedSequence);
        }

        await Assert.That(await iterator.MoveNextAsync()).IsFalse();
        return expectedError is null ? TerminalReply(terminal) : FailedReply(terminal);
    }

    private static async Task AssertStartedAsync(
        CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, Guid requestId)
    {
        await Assert.That(chunk.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(chunk.Sequence).IsEqualTo(StartedSequence);
        await Assert.That(chunk.EventType).IsEqualTo(
            CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(CqrsStreamChunkKind.Started));
        await Assert.That(chunk.ProgressResult?.Value?.RequestId).IsEqualTo(requestId);
        await Assert.That(chunk.Final).IsNull();
    }

    private static async Task AssertCompletedAsync(
        CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, int sequence)
    {
        await Assert.That(chunk.Kind).IsEqualTo(CqrsStreamChunkKind.Completed);
        await Assert.That(chunk.Sequence).IsEqualTo(sequence);
        await Assert.That(chunk.EventType).IsEqualTo(
            CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(CqrsStreamChunkKind.Completed));
        await Assert.That(chunk.ProgressResult).IsNull();
        await Assert.That(chunk.Final?.IsSuccess).IsTrue();
        await Assert.That(chunk.Final?.Problem).IsNull();
    }

    private static async Task AssertFailedAsync(
        CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, int sequence, ErrorCode expectedError)
    {
        await Assert.That(chunk.Kind).IsEqualTo(CqrsStreamChunkKind.Failed);
        await Assert.That(chunk.Sequence).IsEqualTo(sequence);
        await Assert.That(chunk.EventType).IsEqualTo(
            CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(CqrsStreamChunkKind.Failed));
        await Assert.That(chunk.ProgressResult).IsNull();
        await Assert.That(chunk.Final?.IsSuccess).IsFalse();
        await Assert.That(chunk.Final?.Value).IsNull();
        var problem = chunk.Final?.Problem ?? throw new InvalidOperationException("The failure terminal has no Problem.");
        await Assert.That(GrainRequestStreamProblem.ReadCode(problem, UnitRoutingOptions.Routing())).IsEqualTo(expectedError);
        await Assert.That((problem.Detail ?? string.Empty).Length).IsGreaterThan(0);
    }

    private static GrainOperationReply TerminalReply(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk)
        => chunk.Final?.Value ?? throw new InvalidOperationException("The completed request has no final value.");

    private static GrainOperationReply FailedReply(
        CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk)
    {
        var problem = chunk.Final?.Problem ?? throw new InvalidOperationException("The failed request has no Problem.");
        return new()
        {
            Error = GrainRequestStreamProblem.ReadCode(problem, UnitRoutingOptions.Routing()),
            SafeDetail = problem.Detail
        };
    }
}
