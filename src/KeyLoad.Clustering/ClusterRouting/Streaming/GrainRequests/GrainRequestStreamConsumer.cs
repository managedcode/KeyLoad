using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal static class GrainRequestStreamConsumer
{
    internal static Task<GrainOperationReply> DrainAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, IOptions<GrainRoutingOptions> options, CancellationToken cancellationToken)
        => DrainCoreAsync(createStream, serializer, requestId, clock, options, null, null, cancellationToken);

    internal static Task<GrainOperationReply> DrainWithPurposeAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, IOptions<GrainRoutingOptions> options,
        NativeCqrsStreamPurpose purpose, CancellationToken cancellationToken)
        => DrainCoreAsync(createStream, serializer, requestId, clock, options, purpose, null, cancellationToken);

    internal static Task<GrainOperationReply> DrainWithProgressAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, IOptions<GrainRoutingOptions> options,
        NativeCqrsStreamPurpose purpose, Func<GrainRequestProgress, CancellationToken, ValueTask> progress,
        CancellationToken cancellationToken)
        => DrainCoreAsync(createStream, serializer, requestId, clock, options, purpose, progress, cancellationToken);

    private static async Task<GrainOperationReply> DrainCoreAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, IOptions<GrainRoutingOptions> options,
        NativeCqrsStreamPurpose? purpose, Func<GrainRequestProgress, CancellationToken, ValueTask>? progress,
        CancellationToken cancellationToken)
    {
        GrainOperationReply? terminal;
        var bounded = NativeCqrsStreamLifetime.RunWithPurpose(
            createStream: token => createStream(token).WithBatchSize(GrainRequestStreamProtocol.BatchSize),
            serializer: serializer, requestId: requestId, clock: clock, settled: static () => { }, cancellationToken: cancellationToken, options: options, owner: null, purpose: purpose ?? new NativeCqrsStreamPurpose());
        try
        {
            terminal = await ReadTerminalAsync(bounded, options, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested
            && error.CancellationToken != cancellationToken && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            throw new OperationCanceledException(error.Message, error, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return terminal ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
    }

    private static async Task<GrainOperationReply?> ReadTerminalAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> bounded,
        IOptions<GrainRoutingOptions> options, Func<GrainRequestProgress, CancellationToken, ValueTask>? progress,
        CancellationToken cancellationToken)
    {
        GrainOperationReply? terminal = null;
        await foreach (var chunk in bounded.ConfigureAwait(false))
        {
            if (chunk.Kind == CqrsStreamChunkKind.Progress && progress is not null)
            {
                await progress(chunk.ProgressResult!.Value.Value!, cancellationToken).ConfigureAwait(false);
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
                    Error = GrainRequestStreamProblem.ReadCode(problem: problem, options: options),
                    SafeDetail = problem.Detail
                };
            }
        }
        return terminal;
    }
}
