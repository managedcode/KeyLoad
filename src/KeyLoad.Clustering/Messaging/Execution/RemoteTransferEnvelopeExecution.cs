using KeyLoad.Core.Messaging;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal sealed class RemoteTransferEnvelopeExecution(GrainRequestCodec codec, BackgroundStorageClient storage,
    RemoteTransferCoordinationHint hint,
    IServiceProvider services, IGrainFactory grainFactory, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing)
{
    internal async Task<T?> ReadAsync<T>(string subject, GrainReadKind kind, ReadOnlyMemory<byte> payload, CancellationToken token) where T : class
    {
        var requestId = Guid.NewGuid();
        var principal = await CurrentPrincipalAsync(subject, token).ConfigureAwait(true);
        var connection = ConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, Guid.Empty, token, connectionId: connection);
        var envelope = codec.CreateRead(requestId, principal.Id, kind, payload);
        var reply = await DrainAsync(connection, requestId, envelope, token).ConfigureAwait(true);
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? RemoteTransferCoordinationProtocol.InvalidResult);
        }
        var value = OrleansSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value;
        return value is null ? null : value as T ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferCoordinationProtocol.InvalidResult);
    }

    internal async Task<GrainOperationReply> ApplyAsync(string subject, CommandRequest request, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var principal = await CurrentPrincipalAsync(subject, token).ConfigureAwait(true);
        var connection = ConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, request.CommandId, token, connectionId: connection);
        var envelope = codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, request.CommandId, OrleansSerialization.Serialize(request));
        return await DrainAsync(connection, requestId, envelope, token).ConfigureAwait(true);
    }

    private async Task<PrincipalRecord> CurrentPrincipalAsync(string subject, CancellationToken token)
    {
        var principal = await storage.Operations.ReadTransferCoordinatorAsync(hint, token).ConfigureAwait(true);
        if (principal.Id != subject)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        }
        return principal;
    }

    private Task<GrainOperationReply> DrainAsync(Guid connection, Guid requestId, GrainRequestEnvelope envelope, CancellationToken token)
        => GrainRequestStreamConsumer.DrainAsync(original => grainFactory.GetGrain<IConnectionGrain>(connection).ExecuteStreamAsync(envelope, original),
            serializer, requestId, clock, routing, token);
}
