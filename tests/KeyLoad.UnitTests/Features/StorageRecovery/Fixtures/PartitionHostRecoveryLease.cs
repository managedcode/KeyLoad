using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Server;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionHostRecoveryLease(PartitionHostRecoveryAttempt attempt) : IAsyncDisposable
{
    private PartitionHost? host;
    private Task? opening;
    private Task? closing;
    internal PartitionHost Host => host ?? throw new ObjectDisposedException(nameof(PartitionHostRecoveryLease));

    internal Task OpenAsync(NodeOptions options, ILogger<ReplicaConsensus> logger)
        => opening ??= OpenCoreAsync(options, logger);

    private async Task OpenCoreAsync(NodeOptions options, ILogger<ReplicaConsensus> logger)
    {
        try
        {
            host = await PartitionHost.OpenAsync(ServerRuntimeTestOptions.Runtime(options), new AuthorizationPolicy(),
                new CommandAdmissionGovernor(UnitAdmissionOptions.Command(options.CommandAdmission)), TimeProvider.System,
                logger, attempt.Ownership).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            attempt.RecordFailure(failure);
            throw;
        }
    }

    public ValueTask DisposeAsync() => new(closing ??= CloseCoreAsync());

    private async Task CloseCoreAsync()
    {
        if (opening is { } original)
        {
            await original.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        if (host is not null)
        {
            try
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                attempt.RecordFailure(failure);
                throw;
            }
        }
        attempt.RequireReleased();
    }
}
