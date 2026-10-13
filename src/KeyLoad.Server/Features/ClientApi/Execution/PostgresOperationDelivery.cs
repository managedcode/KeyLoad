using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class PostgresOperationDelivery
{
    internal static async Task ExecuteAsync(IServiceProvider services, PostgresWireWriter writer,
        ServerConnectionFeature owner, PostgresSessionIdentity identity, PartitionRef partition,
        PostgresBoundPortal portal, int rows, PostgresCancellationSession cancellation,
        PostgresSessionOptions options, PostgresRetainedAdmission admission, bool describe, CancellationToken closed)
    {
        var original = cancellation.Begin(closed);
        var failures = new List<Exception>();
        var delivering = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new PostgresTransportDeadline(
                TimeSpan.FromSeconds(services.GetRequiredService<IOptions<DatabaseLimits>>().Value.QueryDeadlineSeconds),
                services.GetRequiredService<TimeProvider>(), original);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var reply = await PostgresOperationExecutor.ExecuteAsync(services, owner, identity,
                    partition, portal, rows, admission, deadline.Token).ConfigureAwait(false);
                delivering = true;
                await PostgresSessionReplies.ResultAsync(writer, reply, portal.ResultFormat, options,
                    services.GetRequiredService<IOptions<McpExecutionOptions>>().Value.IngressScratchBytes,
                    describe, deadline.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (delivering && failures.Count != PostgresProtocol.AllRows)
        { ServerFailureObserver.Observe(owner.RequestClose, failures); }
        await ServerFailureObserver.ObserveAsync(cancellation.EndAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
