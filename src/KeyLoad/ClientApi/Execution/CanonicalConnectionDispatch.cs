using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class CanonicalConnectionDispatch
{
    internal static async Task<object?> ExecuteAsync(IServiceProvider services, ServerConnectionFeature owner,
        PrincipalRecord principal, McpDecodedOperation operation, CancellationToken token)
    {
        var actual = CanonicalOperationIssuance.Create(services, principal, operation.ReadKind,
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
                    var reply = await node.ExecuteAsync(actual.RequestId, actual.Envelope, actual.Command,
                        connection.Token);
                    var value = GrainReplyFactory.RequireValue(reply).Value;
                    connection.Token.ThrowIfCancellationRequested();
                    result = (PublicReadResponse.Create(operation.ReadKind, operation.CommandKind, value)).Value;
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
}
