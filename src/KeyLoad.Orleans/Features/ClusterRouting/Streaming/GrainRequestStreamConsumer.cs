using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal static class GrainRequestStreamConsumer
{
    internal static async Task<GrainOperationReply> DrainAsync(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, CancellationToken cancellationToken, IOptions<GrainRoutingOptions> options)
    {
        GrainOperationReply? terminal = null;
        var bounded = NativeCqrsStreamLifetime.Run(
            token => createStream(token).WithBatchSize(GrainRequestStreamProtocol.BatchSize),
            serializer, requestId, clock, static () => { }, cancellationToken, options);
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
                    Error = GrainRequestStreamProblem.ReadCode(problem),
                    SafeDetail = problem.Detail
                };
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return terminal ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
    }
}
