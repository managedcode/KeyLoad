using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWireWaveStartup
{
    internal static async Task<TwoRf3MembershipWave> StartAsync(CancellationToken token,
        LocalRf3ImageSelection.Selection? selection = null, Action<string>? observe = null)
    {
        var preparation = selection is null
            ? await LocalRf3ImageTestSession.StartIfSelectedAsync(token).ConfigureAwait(false) : null;
        selection ??= preparation?.Selection ?? LocalRf3ImageSelection.Read();
        var wave = new TwoRf3MembershipWave(selection, register: true, remote: true, query: true,
            probe: true, protectedDocument: true)
        {
            nativeBlobWireProbeSelected = true,
            localImagePreparation = preparation
        };
        return await TwoRf3MembershipWaveStartup.StartOwnedAsync(wave, token, observe).ConfigureAwait(false);
    }
}
