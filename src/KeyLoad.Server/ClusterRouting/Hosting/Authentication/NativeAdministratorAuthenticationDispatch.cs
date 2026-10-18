using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Restores the actual native identity context while retaining both request and restoration failures.</summary>
internal static class NativeAdministratorAuthenticationDispatch
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
                        cancellationToken: token).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }
            finally
            {
                ServerFailureObserver.Observe(identity.Dispose, failures);
            }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply!;
    }
}
