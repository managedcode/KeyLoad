using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class CanonicalConnectionDispatch
{
    internal static async Task<object?> ExecuteAsync(IServiceProvider services, ServerConnectionFeature owner,
        PrincipalRecord principal, McpDecodedOperation operation, CancellationToken token)
    {
        var actual = CanonicalOperationSigning.Create(services, principal, operation.ReadKind,
            operation.CommandKind, operation.CommandId, operation.Payload, token);
        var node = services.GetRequiredService<OrleansNode>();
        object? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var connection = owner.Acquire(token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var identity = node.OpenRequestContext(principal, actual.RequestId, operation.CommandId,
                    connection.Token, connection.Id);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var reply = await node.ExecuteAsync(actual.RequestId, actual.SignedRequest, actual.Command,
                        connection.Token).ConfigureAwait(false);
                    var value = NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value;
                    global::KeyLoad.Server.Features.GraphTraversal.RemoteGraphPublicResult.RequireComplete(
                        operation.CommandKind, operation.CommandId, reply, value);
                    connection.Token.ThrowIfCancellationRequested();
                    result = PublicReadResponse.Create(operation.ReadKind, operation.CommandKind, value);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
}
