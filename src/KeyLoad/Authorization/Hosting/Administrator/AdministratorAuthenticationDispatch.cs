using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Restores the actual execution identity context while retaining both request and restoration failures.</summary>
internal static class AdministratorAuthenticationDispatch
{
    internal static async Task<GrainOperationReply> SendAsync(OrleansNode node, Guid requestId,
        string signed, Action onDispatch, CancellationToken token)
    {
        var failures = new List<Exception>();
        GrainOperationReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var identity = node.OpenRequestContext(null, requestId, Guid.Empty, token);
            try
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    onDispatch();
                    reply = await node.ExecuteStartupRequestAsync(requestId, signed, command: false,
                        cancellationToken: token);
                }, failures);
            }
            finally
            {
                ServerFailureObserver.Observe(identity.Dispose, failures);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply!;
    }
}
