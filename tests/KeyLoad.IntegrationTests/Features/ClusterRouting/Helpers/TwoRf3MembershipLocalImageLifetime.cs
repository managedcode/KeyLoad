using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipLocalImageLifetime
{
    internal static async Task<TwoRf3MembershipWave> StartProtectedAsync(CancellationToken token)
    {
        var preparation = await LocalRf3ImageTestSession.StartIfSelectedAsync(token).ConfigureAwait(false);
        var selection = preparation?.Selection ?? LocalRf3ImageSelection.Read();
        var wave = new TwoRf3MembershipWave(selection, register: true, remote: true, query: true,
            probe: true, protectedDocument: true)
        { localImagePreparation = preparation };
        return await TwoRf3MembershipWave.StartOwnedAsync(wave, token).ConfigureAwait(false);
    }

    internal static async Task JoinAsync(TwoRf3MembershipWave wave, List<Exception> failures)
    {
        if (wave.localImagePreparation is not { } preparation)
        { return; }
        if (wave.startAttempted && (!wave.applicationDisposed || !wave.nodeLocksReleased))
        { return; }
        var before = failures.Count;
        await ServerFailureObserver.ObserveAsync(
            () => preparation.DisposeAsync(removeImage: failures.Count == 0).AsTask(), failures).ConfigureAwait(false);
        if (failures.Count == before)
        { wave.localImagePreparation = null; }
    }
}
