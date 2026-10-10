using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal sealed class RemoteTransferSignedExecution(GrainRequestCodec codec, DatabaseEngine database,
    IServiceProvider services, IGrainFactory grainFactory, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing)
{
    internal async Task<T?> ReadAsync<T>(string subject, GrainReadKind kind, ReadOnlyMemory<byte> payload, CancellationToken token) where T : class
    {
        var requestId = Guid.NewGuid();
        var principal = database.Store.Read(view => database.Principal(view, subject, clock.GetUtcNow()));
        var connection = NativeConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, Guid.Empty, token, connectionId: connection);
        var signed = codec.CreateRead(requestId, principal.Id, kind, payload);
        var reply = await DrainAsync(connection, requestId, signed, token).ConfigureAwait(true);
        if (reply.Error is { } error)
        { throw Errors.Fail(error, reply.SafeDetail ?? RemoteTransferCoordinationProtocol.InvalidResult); }
        var value = NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value;
        return value is null ? null : value as T ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferCoordinationProtocol.InvalidResult);
    }

    internal async Task<GrainOperationReply> ApplyAsync(string subject, CommandRequest request, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var principal = database.Store.Read(view => database.Principal(view, subject, clock.GetUtcNow()));
        var connection = NativeConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, request.CommandId, token, connectionId: connection);
        var signed = codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, request.CommandId, NativeSerialization.Serialize(request));
        return await DrainAsync(connection, requestId, signed, token).ConfigureAwait(true);
    }

    private Task<GrainOperationReply> DrainAsync(Guid connection, Guid requestId, string signed, CancellationToken token)
        => GrainRequestStreamConsumer.DrainAsync(original => grainFactory.GetGrain<IConnectionGrain>(connection).ExecuteStreamAsync(signed, original),
            serializer, requestId, clock, routing, token);
}
