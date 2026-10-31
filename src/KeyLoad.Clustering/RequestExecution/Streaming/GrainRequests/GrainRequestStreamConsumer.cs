using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal static class GrainRequestStreamConsumer
{
    internal static Task<GrainOperationReply> DrainAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Guid requestId, CancellationToken cancellationToken)
        => DrainCoreAsync(createStream, requestId, null, null, cancellationToken);

    internal static Task<GrainOperationReply> DrainWithPurposeAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Guid requestId, CqrsStreamPurpose purpose, CancellationToken cancellationToken)
        => DrainCoreAsync(createStream, requestId, purpose, null, cancellationToken);

    internal static Task<GrainOperationReply> DrainWithProgressAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Guid requestId, CqrsStreamPurpose purpose, Func<GrainRequestProgress, CancellationToken, ValueTask> progress,
        CancellationToken cancellationToken)
        => DrainCoreAsync(createStream, requestId, purpose, progress, cancellationToken);

    private static async Task<GrainOperationReply> DrainCoreAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Guid requestId, CqrsStreamPurpose? purpose, Func<GrainRequestProgress, CancellationToken, ValueTask>? progress,
        CancellationToken cancellationToken)
    {
        GrainOperationReply? terminal;
        var bounded = CqrsStreamLifetime.RunWithPurpose(
            createStream: token => createStream(token).WithBatchSize(GrainRequestStreamProtocol.BatchSize),
            requestId: requestId, settled: static () => { }, owner: null, purpose: purpose ?? new CqrsStreamPurpose(),
            cancellationToken: cancellationToken);
        try
        {
            terminal = await ReadTerminalAsync(bounded, progress, cancellationToken);
        }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested
            && error.CancellationToken != cancellationToken && CqrsBoundaryErrors.IsNonFatal(error))
        {
            throw new OperationCanceledException(error.Message, error, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return terminal ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
    }

    private static async Task<GrainOperationReply?> ReadTerminalAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> bounded,
        Func<GrainRequestProgress, CancellationToken, ValueTask>? progress,
        CancellationToken cancellationToken)
    {
        GrainOperationReply? terminal = null;
        await foreach (var chunk in bounded)
        {
            if (chunk.Kind == CqrsStreamChunkKind.Progress && progress is not null)
            {
                await progress(chunk.ProgressResult!.Value.Value!, cancellationToken);
            }
            if (chunk.Kind == CqrsStreamChunkKind.Completed)
            {
                terminal = chunk.Final!.Value.Value!;
            }
            else if (chunk.Kind == CqrsStreamChunkKind.Failed)
            {
                var problem = chunk.Final!.Value.Problem!;
                terminal = new GrainOperationReply
                {
                    Error = GrainRequestStreamProblem.ReadCode(problem),
                    SafeDetail = problem.Detail
                };
            }
        }
        return terminal;
    }
}
