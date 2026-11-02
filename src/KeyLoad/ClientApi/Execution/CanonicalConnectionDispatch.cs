using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class CanonicalConnectionDispatch
{
    internal static async Task<object?> /* KLD0041: Return the terminal public DTO after mapping inside joined connection/context cleanup; PostgreSQL performs bounded JSON encoding afterwards and classifies possible-write encoding failures as unknown outcomes. */ ExecuteAsync(IServiceProvider services, ServerConnectionFeature owner,
        PrincipalRecord principal, McpDecodedOperation operation, CancellationToken token)
    {
        var actual = (CanonicalOperationIssuance.Create(services, principal, operation.ReadKind,
            operation.CommandKind, operation.CommandId, (operation.Payload /* KLD0041: Use the already validated decoded operation input selected by its closed MCP/SQL kind when issuing the current generated envelope. */), token) /* KLD0041: Issue the already decoded heterogeneous MCP/SQL operation into the current generated request slot without an extra object result carrier. */);
        var node = services.GetRequiredService<OrleansNode>();
        object? /* KLD0041: Hold the terminal public DTO produced inside the joined connection/context cleanup scope until PostgreSQL performs bounded JSON encoding afterwards. */ result = null;
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
                    var /* KLD0041: Read the existing generated GrainValue runtime reply slot once before mapping it to the final public DTO inside joined cleanup. */ value = (GrainReplyFactory.RequireValue(reply).Value /* KLD0041: Read the current generated GrainValue reply slot at the terminal DTO mapping boundary; preserve the existing binary reply layout. */);
                    connection.Token.ThrowIfCancellationRequested();
                    result = (((PublicReadResponse.Create(operation.ReadKind, operation.CommandKind, value) /* KLD0041: Map the current runtime reply to the concrete terminal public DTO while the joined connection/context cleanup scope is still active. */)).Value /* KLD0041: Retain the terminal public DTO from the existing response projection until bounded PostgreSQL JSON encoding after joined cleanup. */);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
}
