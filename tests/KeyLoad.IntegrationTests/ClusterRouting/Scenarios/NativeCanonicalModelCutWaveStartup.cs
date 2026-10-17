using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCanonicalModelCutWaveStartup
{
    internal static Task<TwoRf3MembershipWave> StartAsync(string technicalPrincipalId, CancellationToken token,
        LocalRf3ImageSelection.Selection? selection = null, Action<string>? observe = null)
    {
        JsonData.Identifier(technicalPrincipalId);
        var wave = new TwoRf3MembershipWave(selection, register: true, remote: true,
            query: true, probe: true, protectedDocument: true)
        {
            nativeCanonicalCutsSelected = true,
            remoteTransferPrincipalId = technicalPrincipalId
        };
        return TwoRf3MembershipWaveStartup.StartOwnedAsync(wave, token, observe);
    }

    internal static async Task PrepareAsync(TwoRf3MembershipWave wave, string root)
    {
        if (!wave.nativeCanonicalCutsSelected)
        {
            if (wave.canonicalCutControls is not null)
            {
                throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
            }
            return;
        }
        if (wave.canonicalCutControls is null)
        {
            wave.canonicalCutControls = NativeCanonicalModelCutFixture.Create(wave, root);
        }
        else
        {
            await wave.canonicalCutControls.ReopenAfterOwnersJoinedAsync(wave).ConfigureAwait(false);
        }
    }
}
