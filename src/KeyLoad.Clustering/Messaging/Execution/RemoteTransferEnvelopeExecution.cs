using KeyLoad.Core.Messaging;

namespace KeyLoad.Clustering;

internal sealed class RemoteTransferEnvelopeExecution(GrainRequestCodec codec, BackgroundStorageClient storage,
    RemoteTransferCoordinationHint hint,
    IServiceProvider services, IGrainFactory grainFactory)
{
    internal async Task<(bool HasValue, T? Value)> ReadAsync<T, TRequest>(string subject, GrainReadKind kind, TRequest payload, CancellationToken token) where T : class where TRequest : notnull
    {
        var requestId = Guid.NewGuid();
        var principal = await CurrentPrincipalAsync(subject, token);
        var connection = ConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, Guid.Empty, token, connectionId: connection);
        var envelope = codec.CreateRead(requestId, principal.Id, kind, payload);
        var reply = await DrainAsync(connection, requestId, envelope, token);
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? RemoteTransferCoordinationProtocol.InvalidResult);
        }
        var /* KLD0041: Inspect the current generated GrainValue slot for an absent or correctly typed transfer response before using it. */ value = (GrainReplyFactory.RequireValue(reply).Value /* KLD0041: Read the current generated GrainValue field only to validate the concrete RPC response or its permitted absence. */);
        return value is null ? (false, default(T?)) : (true, ((T?)((value as T /* KLD0042: Validate the current generated GrainValue slot as T? before accepting this concrete RPC response. */) ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferCoordinationProtocol.InvalidResult))));
    }

    internal async Task<GrainOperationReply> ApplyAsync(string subject, CommandRequest request, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var principal = await CurrentPrincipalAsync(subject, token);
        var connection = ConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, request.CommandId, token, connectionId: connection);
        var envelope = codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, request.CommandId, request);
        return await DrainAsync(connection, requestId, envelope, token);
    }

    private async Task<PrincipalRecord> CurrentPrincipalAsync(string subject, CancellationToken token)
    {
        var principal = await storage.Operations.ReadTransferCoordinatorAsync(hint, token);
        if (principal.Id != subject)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        }
        return principal;
    }

    private Task<GrainOperationReply> DrainAsync(Guid connection, Guid requestId, GrainRequestEnvelope envelope, CancellationToken token)
        => GrainRequestStreamConsumer.DrainAsync(original => grainFactory.GetGrain<IConnectionGrain>(connection).ExecuteStreamAsync(envelope, original), requestId, token);
}
