using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class NativeBlobWholeParentOwner(List<Exception> failures) : IAsyncDisposable
{
    private const int NoFailures = 0;
    private readonly List<RemoteTransferNativeMembership> memberships = [];
    private TwoRf3MembershipWave? wave;
    private Task? producer;
    private Task? disposal;
    internal TwoRf3MembershipWave Wave => wave ?? throw new InvalidOperationException("The original Blob wave is absent.");
    internal RemoteTransferNativeAdministrator Administrator { get; } = new();

    internal Task RunAsync(CancellationToken token)
        => producer = NativeBlobWholeParentFlow.ExecuteAsync(this, token);

    internal async Task StartAsync(CancellationToken token)
    {
        wave = await NativeBlobWireWaveStartup.StartAsync(token).ConfigureAwait(false);
        await Administrator.InitializeAsync(wave, token).ConfigureAwait(false);
    }

    internal async Task<RemoteTransferNativeMembership> ReadMembershipAsync(CancellationToken token)
    {
        var actual = await RemoteTransferNativeMembership.ReadAsync(Wave, token).ConfigureAwait(false);
        memberships.Add(actual);
        return actual;
    }

    internal Task CloseCallersAsync() => Administrator.DisposeAsync().AsTask();

    internal async Task<IReadOnlyDictionary<string, NativeBlobWholeOwnerCut>> CaptureAsync(
        IReadOnlyDictionary<string, ReplicaSiloDiscovery> signedOwners, CancellationToken token)
    {
        await CloseCallersAsync().ConfigureAwait(false);
        return await NativeBlobWholeCold.CaptureAsync(Wave, signedOwners, token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        var cleanup = new List<Exception>();
        if (producer is { } original)
        { await ServerFailureObserver.ObserveAsync(() => original, cleanup); }
        await ServerFailureObserver.ObserveAsync(CloseCallersAsync, cleanup);
        if (wave is { } actual)
        {
            if (failures.Count != NoFailures || cleanup.Count != NoFailures) { actual.RetainRoots(); }
            await ServerFailureObserver.ObserveAsync(() => actual.DisposeAsync().AsTask(), cleanup);
            if (actual.applicationDisposed && actual.nodeLocksReleased)
            {
                foreach (var membership in memberships)
                { ServerFailureObserver.Observe(membership.Dispose, cleanup); }
            }
            else
            {
                actual.RetainRoots();
                cleanup.Add(new InvalidOperationException("Original Blob owners did not join; retain borrowed membership keys."));
            }
        }
        foreach (var error in cleanup)
        { if (!failures.Any(prior => ReferenceEquals(prior, error))) { failures.Add(error); } }
    }
}
