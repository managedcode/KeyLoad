using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMinimumRf3Flow
{
    internal static async Task CurrentAsync(TwoRf3MembershipWave wave, ChunkMinimumRf3State state, CancellationToken token)
    {
        foreach (var node in state.Nodes)
        {
            await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, state.Administrator,
                administrator => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, state.Reader.Secret,
                    async reader =>
                    {
                        await McpDiscoveryAssertions.VerifyAsync(await reader.Mcp.Client.DiscoverKeyLoadToolAsync(
                            SampleChunkProtocol.ReadTool, token));
                        foreach (var window in state.Windows)
                        {
                            foreach (var official in new[] { false, true })
                            {
                                var request = window.Request(state.Partition) with
                                {
                                    MinimumToken = window.Minimum
                                };
                                var current = window.Minimum.Incarnation == state.Owner.Incarnation
                                    && window.Minimum.OwnershipEpoch == state.Owner.PlacementEpoch;
                                await ChunkMinimumRf3Pages.ObserveAsync(() => current
                                    ? ChunkMinimumRf3Pages.ReadAsync(reader, window, request, official, token)
                                    : ChunkMinimumRf3Refusals.FailureAsync(reader, request, ErrorCode.TokenInvalidated,
                                        ChunkMinimumRf3Refusals.WrongIncarnation, official, token), administrator, state, token);
                                await ChunkMinimumRf3Pages.ObserveAsync(() => ChunkMinimumRf3Pages.ReadAsync(reader,
                                    window, window.Request(state.Partition), official, token), administrator, state, token);
                            }
                            if (window.Minimum.Incarnation == state.Owner.Incarnation)
                            {
                                await ChunkMinimumRf3Refusals.RunAsync(reader, administrator, state, window, token);
                            }
                        }
                        return true;
                    }, token), token);
        }
        await ReplayAsync(wave, state, token);
    }

    private static async Task ReplayAsync(TwoRf3MembershipWave wave, ChunkMinimumRf3State state, CancellationToken token)
    {
        foreach (var original in state.Originals)
        {
            await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, original.Node, state.Administrator,
                source => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, state.Nodes.First(),
                    state.Administrator, async administrator =>
                    {
                        foreach (var official in new[] { false, true })
                        {
                            await ChunkMinimumRf3Pages.ObserveAsync(async () =>
                            {
                                var actual = official
                                    ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await source.Mcp.CallAsync(
                                        McpCallerTools.DocumentsCommit, original.Request, token))).Value
                                    : await McpCallerAssertions.SdkSuccessAsync(await source.Sdk.CommitAsync(original.Request, token));
                                await SqlRf3Protocol.EqualAsync(original.Receipt, actual);
                            }, administrator, state, token);
                        }
                        return true;
                    }, token), token);
        }
    }
}
