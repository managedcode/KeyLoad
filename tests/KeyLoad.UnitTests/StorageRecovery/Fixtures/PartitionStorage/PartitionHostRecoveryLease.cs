using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Server;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionHostRecoveryLease(PartitionHostRecoveryAttempt attempt) : IAsyncDisposable
{
    private PartitionHost? host;
    private RequestCqrsHostedRuntimeFixture? native;
    private Task? opening;
    private Task? closing;
    internal PartitionHost Host => host ?? throw new ObjectDisposedException(nameof(PartitionHostRecoveryLease));

    internal Task OpenAsync(NodeOptions options, ILogger<PartitionHost> logger)
        => opening ??= OpenCoreAsync(options, logger);

    private async Task OpenCoreAsync(NodeOptions options, ILogger<PartitionHost> logger)
    {
        try
        {
            native = new();
            await native.InitializeAsync(TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
            host = await PartitionHost.OpenAsync(ServerRuntimeTestOptions.Runtime(options), new AuthorizationPolicy(),
                new CommandAdmissionGovernor(UnitAdmissionOptions.Command(options.CommandAdmission)), TimeProvider.System,
                logger, native.Grains, attempt.Ownership).ConfigureAwait(false);
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
        var failures = new List<Exception>();
        if (host is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => host.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (native is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => native.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        foreach (var failure in failures)
        {
            attempt.RecordFailure(failure);
        }
        ServerFailureObserver.Observe(attempt.RequireReleased, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
