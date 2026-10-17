using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ResourceExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleRf3Warm
{
    private static readonly DropLifecycleRf3Route[] Routes =
        [DropLifecycleRf3Route.Sdk, DropLifecycleRf3Route.Mcp, DropLifecycleRf3Route.SqlMcp];

    internal static async Task<DropTableLifecycleWaveState> RunAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        var nativeSeed = await ResourcePartitionInventoryRf3Corpus.CreateAsync(wave, token).ConfigureAwait(false);
        using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token).ConfigureAwait(false);
        return await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            nativeSeed.Credential, async administrator =>
            {
                var state = await DropTableLifecycleSeed.CreateAsync(wave, nativeSeed, membership, administrator, token).ConfigureAwait(false);
                var deniedCredential = await DropTableLifecycleAuthorization.PersistAsync(wave, state.Partition, token).ConfigureAwait(false);
                await DeniedAsync(wave, deniedCredential, administrator, state, token).ConfigureAwait(false);
                var drops = ImmutableArray.CreateBuilder<DropTableLifecycleCompletedDrop>();
                for (var index = 0; index < state.Tables.Length; index++)
                {
                    var table = state.Tables[index];
                    var oldReferences = await DropTableLifecycleReferences.CaptureAsync(administrator, table, token).ConfigureAwait(false);
                    var parent = Guid.NewGuid();
                    var original = await DropLifecycleRf3Calls.InvokeAsync(administrator, parent, table.Request,
                        state.Partition, Routes[index], token).ConfigureAwait(false);
                    var fresh = await DropTableLifecycleFresh.CreateAsync(administrator, state, table,
                        () => DropTableLifecycleSeed.InventoryAsync(wave, nativeSeed, membership, table.Name, token), token).ConfigureAwait(false);
                    drops.Add(new(table, parent, original, fresh, oldReferences));
                }
                var completed = new DropTableLifecycleWaveState(nativeSeed, state, deniedCredential, drops.ToImmutable());
                await DropTableLifecycleFreshReplay.OriginalAsync(administrator, state.Original, state.Receipt,
                    token, () => DropTableLifecycleRf3Effects.VerifyAsync(administrator, completed, token)).ConfigureAwait(false);
                return completed;
            }, token).ConfigureAwait(false);
    }

    private static async Task DeniedAsync(TwoRf3MembershipWave wave, string credential,
        RequestCqrsRf3Callers administrator, DropTableLifecycleSeedState state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node4,
            credential, async denied =>
            {
                foreach (var table in state.Tables)
                {
                    await DropTableLifecycleAuthorization.DeniedAsync(denied, state.Partition, table.Name,
                        table.Rows[0].Reference, state.Original,
                        () => UnchangedAsync(administrator, state, token), token).ConfigureAwait(false);
                }
                return true;
            }, token).ConfigureAwait(false);
    }

    private static async Task UnchangedAsync(RequestCqrsRf3Callers administrator,
        DropTableLifecycleSeedState state, CancellationToken token)
    {
        await DropTableLifecycleSeedAssertions.BeforeAsync(administrator, state, token).ConfigureAwait(false);
        await DropTableLifecycleFreshReplay.OriginalAsync(administrator, state.Original, state.Receipt, token).ConfigureAwait(false);
        await DropTableLifecycleSeedAssertions.BeforeAsync(administrator, state, token).ConfigureAwait(false);
    }
}
