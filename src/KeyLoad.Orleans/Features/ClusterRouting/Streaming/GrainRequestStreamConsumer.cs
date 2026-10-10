using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal static class GrainRequestStreamConsumer
{
    internal static Task<GrainOperationReply> DrainAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, IOptions<GrainRoutingOptions> options, CancellationToken cancellationToken)
        => DrainCoreAsync(createStream, serializer, requestId, clock, options, null, cancellationToken);

    internal static Task<GrainOperationReply> DrainWithPurposeAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, IOptions<GrainRoutingOptions> options,
        NativeCqrsStreamPurpose purpose, CancellationToken cancellationToken)
        => DrainCoreAsync(createStream, serializer, requestId, clock, options, purpose, cancellationToken);

    private static async Task<GrainOperationReply> DrainCoreAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, IOptions<GrainRoutingOptions> options,
        NativeCqrsStreamPurpose? purpose, CancellationToken cancellationToken)
    {
        GrainOperationReply? terminal = null;
        var bounded = NativeCqrsStreamLifetime.RunWithPurpose(
            createStream: token => createStream(token).WithBatchSize(GrainRequestStreamProtocol.BatchSize),
            serializer: serializer, requestId: requestId, clock: clock, settled: static () => { }, cancellationToken: cancellationToken, options: options, owner: null, purpose: purpose ?? new NativeCqrsStreamPurpose());
        await foreach (var chunk in bounded.ConfigureAwait(false))
        {
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

        cancellationToken.ThrowIfCancellationRequested();
        return terminal ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
    }
}
