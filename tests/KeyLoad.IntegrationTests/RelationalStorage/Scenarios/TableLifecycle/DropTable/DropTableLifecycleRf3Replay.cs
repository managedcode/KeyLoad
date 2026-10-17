using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleRf3Replay
{
    internal static async Task VerifyAsync(TwoRf3MembershipWave wave, DropTableLifecycleWaveState state,
        ImmutableArray<DropLifecycleRf3ReceiptProof> proofs, CancellationToken token)
    {
        await Assert.That(proofs.Length).IsEqualTo(state.Drops.Length);
        using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token).ConfigureAwait(false);
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node4,
            state.NativeSeed.Credential, async administrator =>
            {
                for (var index = 0; index < state.Drops.Length; index++)
                {
                    var drop = state.Drops[index];
                    await DropLifecycleRf3Replay.AllAsync(administrator, drop.ParentId, drop.Table.Request,
                        state.Seed.Partition, proofs[index], drop.Original, token,
                        () => DropTableLifecycleRf3Effects.VerifyAsync(administrator, state, token)).ConfigureAwait(false);
                    var inventory = await DropTableLifecycleSeed.InventoryAsync(wave, state.NativeSeed,
                        membership, drop.Table.Name, token).ConfigureAwait(false);
                    await SqlRf3Protocol.EqualAsync(drop.Fresh.Inventory.Control.Lifetime, inventory.Control.Lifetime);
                    await SqlRf3Protocol.EqualAsync(drop.Fresh.Definition, inventory.Control.Definition);
                    await DropTableLifecycleFresh.VerifyAsync(administrator, state.Seed, drop.Table, drop.Fresh, token).ConfigureAwait(false);
                    await DropTableLifecycleReferences.RefuseAsync(administrator, drop.OldReferences,
                        () => UnchangedAsync(administrator, state.Seed, drop, token), token).ConfigureAwait(false);
                }
                await DropTableLifecycleFreshReplay.OriginalAsync(administrator, state.Seed.Original, state.Seed.Receipt,
                    token, () => DropTableLifecycleRf3Effects.VerifyAsync(administrator, state, token)).ConfigureAwait(false);
                await DeniedAsync(wave, administrator, state, token).ConfigureAwait(false);
                await DropTableLifecycleNativeControls.VerifyAsync(wave, state.NativeSeed,
                    membership, administrator, token).ConfigureAwait(false);
                await DropTableLifecycleSeedAssertions.AfterAsync(administrator, state.Seed, token).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
    }

    private static async Task DeniedAsync(TwoRf3MembershipWave wave, RequestCqrsRf3Callers administrator,
        DropTableLifecycleWaveState state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            state.DeniedCredential, async denied =>
            {
                foreach (var drop in state.Drops)
                {
                    await DropTableLifecycleAuthorization.DeniedAsync(denied, state.Seed.Partition, drop.Table.Name,
                        drop.Fresh.Rows[0].Reference, drop.Fresh.Write,
                        () => UnchangedAsync(administrator, state.Seed, drop, token), token).ConfigureAwait(false);
                }
                return true;
            }, token).ConfigureAwait(false);
    }

    private static async Task UnchangedAsync(RequestCqrsRf3Callers administrator, DropTableLifecycleSeedState seed,
        DropTableLifecycleCompletedDrop drop, CancellationToken token)
    {
        await DropTableLifecycleSeedAssertions.ControlsAsync(administrator, seed, token).ConfigureAwait(false);
        await DropTableLifecycleFreshReplay.VerifyAsync(administrator, drop.Fresh, token).ConfigureAwait(false);
        await DropTableLifecycleSeedAssertions.RowsAsync(administrator, drop.Fresh.Rows, token).ConfigureAwait(false);
        await DropTableLifecycleFreshReads.OldAbsentAsync(administrator, drop.Table, token).ConfigureAwait(false);
    }
}
