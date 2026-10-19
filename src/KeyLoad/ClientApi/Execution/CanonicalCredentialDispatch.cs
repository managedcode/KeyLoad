using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class CanonicalCredentialDispatch
{
    internal static string Prepare(GrainRequestCodec codec, string credential, Guid requestId)
        => codec.CreateRead(requestId, null, GrainReadKind.Authenticate, OrleansSerialization.Serialize(credential));

    internal static async Task<GrainOperationReply> ReadAsync(OrleansNode node, string signed,
        ServerConnectionOperation connection, Guid requestId, Action? onStarted = null)
    {
        GrainOperationReply? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = node.OpenRequestContext(null, requestId, Guid.Empty, connection.Token, connection.Id);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                onStarted?.Invoke();
                result = await node.ExecuteAsync(requestId, signed, false, connection.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, PostgresProtocol.Invalid);
    }
}
