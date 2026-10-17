using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Creates the original six-owner wave with its selected native admission profile.</summary>
internal static class TwoRf3MembershipWaveFactory
{
    internal static Task<TwoRf3MembershipWave> StartAsync(CancellationToken cancellationToken)
        => StartAsync(null, cancellationToken);

    internal static async Task<TwoRf3MembershipWave> StartAsync(LocalRf3ImageSelection.Selection? selection,
        CancellationToken cancellationToken)
    {
        return await TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(selection), cancellationToken).ConfigureAwait(false);
    }

    internal static Task<TwoRf3MembershipWave> StartRegistrationAsync(CancellationToken cancellationToken)
        => TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(null, register: true), cancellationToken);

    internal static Task<TwoRf3MembershipWave> StartRemoteDocumentsAsync(CancellationToken cancellationToken)
        => TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true), cancellationToken);

    internal static Task<TwoRf3MembershipWave> StartProtectedDocumentsAsync(CancellationToken token)
        => TwoRf3MembershipLocalImageLifetime.StartProtectedAsync(token);

    internal static Task<TwoRf3MembershipWave> StartProtectedDocumentsAsync(Action<string> observe, CancellationToken token)
        => TwoRf3MembershipWaveStartup.StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true,
            query: true, probe: true, protectedDocument: true), token, observe);

    internal static Task<TwoRf3MembershipWave> StartNativeCapabilityOmissionAsync(CancellationToken token, LocalRf3ImageSelection.Selection? selection = null,
        Action<string>? observe = null, KeyLoad.IntegrationTests.Features.Messaging.RemoteTransferStartupCallbacks? callbacks = null)
        => TwoRf3MembershipWaveStartup.StartOwnedAsync(new TwoRf3MembershipWave(selection, register: true, remote: true, query: true,
            probe: true, protectedDocument: true)
        {
            nativeDiscoveryOmissionSelected = true,
            startupCallbacks = callbacks
        }, token, observe);

    internal static Task<TwoRf3MembershipWave> StartActivationIsolationAsync(CancellationToken token)
        => TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true, query: true,
            probe: true, protectedDocument: true, isolateActivation: true), token);

    internal static Task<TwoRf3MembershipWave> StartProtectedDocumentsAsync(int maxBatchBytes, CancellationToken token)
    {
        new DatabaseLimits { MaxBatchBytes = maxBatchBytes }.Validate();
        return TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true, query: true,
            probe: true, protectedDocument: true, maximumBatchBytes: maxBatchBytes), token);
    }

    internal static Task<TwoRf3MembershipWave> StartProtectedFramesAsync(int maxFrameBytes, CancellationToken token)
    {
        new KeyLoad.Storage.ZoneTree.ZoneTreeStorageExecutionOptions { MaxFrameBytes = maxFrameBytes }.Validate();
        var wave = new TwoRf3MembershipWave(null, register: true, remote: true, query: true,
            probe: true, protectedDocument: true)
        {
            movementMaxFrameBytes = maxFrameBytes
        };
        return TwoRf3MembershipWave.StartOwnedAsync(wave, token);
    }

    internal static Task<TwoRf3MembershipWave> StartRemoteQueriesAsync(CancellationToken cancellationToken)
        => TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true, query: true), cancellationToken);

    internal static async Task<TwoRf3MembershipWave> StartProbedRemoteQueriesAsync(CancellationToken token)
    {
        var preparation = await LocalRf3ImageTestSession.StartIfSelectedAsync(token).ConfigureAwait(false);
        var selection = preparation?.Selection ?? LocalRf3ImageSelection.Read();
        var wave = new TwoRf3MembershipWave(selection, register: true, remote: true, query: true, probe: true)
        {
            localImagePreparation = preparation
        };
        return await TwoRf3MembershipWave.StartOwnedAsync(wave, token).ConfigureAwait(false);
    }

}
