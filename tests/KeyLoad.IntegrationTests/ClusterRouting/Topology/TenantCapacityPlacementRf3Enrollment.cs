using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Enrollment
{
    internal static async Task<TwoRf3MembershipWave> StartAsync(CancellationToken token)
    {
        var preparation = await LocalRf3ImageTestSession.StartIfSelectedAsync(token).ConfigureAwait(false);
        TwoRf3MembershipWave? wave = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            var selection = preparation?.Selection ?? LocalRf3ImageSelection.Read();
            wave = new TwoRf3MembershipWave(selection, register: true, remote: true, query: true,
                probe: true, protectedDocument: true)
            {
                localImagePreparation = preparation,
                TenantCapacityPlacement = true
            };
        }, failures);
        if (wave is null && preparation is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => preparation.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return await TwoRf3MembershipWave.StartOwnedAsync(wave ?? throw new InvalidOperationException("The actual capacity wave was not allocated."), token).ConfigureAwait(false);
    }
}
