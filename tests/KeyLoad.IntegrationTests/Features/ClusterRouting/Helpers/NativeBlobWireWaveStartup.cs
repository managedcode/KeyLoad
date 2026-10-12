using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWireWaveStartup
{
    internal static Task<TwoRf3MembershipWave> StartAsync(CancellationToken token,
        LocalRf3ImageSelection.Selection? selection = null, Action<string>? observe = null)
    {
        var wave = new TwoRf3MembershipWave(selection, register: true, remote: true, query: true,
            probe: true, protectedDocument: true) { nativeBlobWireProbeSelected = true };
        return TwoRf3MembershipWaveStartup.StartOwnedAsync(wave, token, observe);
    }
}
