using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed record NativeRemoteGraphRf3Operations(RemoteGraphEdgeResultV1 Upsert,
    RemoteGraphEdgeResultV1 Deliver, RemoteGraphEdgeResultV1 Delete)
{
    internal static Task<NativeRemoteGraphRf3Operations> ExecuteAsync(TwoRf3MembershipWave wave,
        NativeRemoteGraphRf3Seed seed, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            seed.Credential, async caller =>
            {
                var upsert = await NativeRemoteGraphPublicCallers.UpsertAndReplayAsync(caller.Sdk, caller.Mcp, seed.Upsert, token);
                await NativeRemoteGraphRf3Assertions.ResultAsync(seed, upsert, 1, false);
                await NativeRemoteGraphRf3Assertions.IncomingAsync(caller, seed, true, token);
                var deliver = await NativeRemoteGraphPublicCallers.DeliverAndReplayAsync(caller.Sdk, caller.Mcp, seed.Deliver, token);
                await NativeRemoteGraphRf3Assertions.ResultAsync(seed, deliver, 1, false);
                await NativeRemoteGraphRf3Assertions.IncomingAsync(caller, seed, true, token);
                var delete = await NativeRemoteGraphPublicCallers.DeleteAndReplayAsync(caller.Sdk, caller.Mcp, seed.Delete, token);
                await NativeRemoteGraphRf3Assertions.ResultAsync(seed, delete, 2, true);
                await NativeRemoteGraphRf3Assertions.IncomingAsync(caller, seed, false, token);
                return new NativeRemoteGraphRf3Operations(upsert, deliver, delete);
            }, token);

    internal Task<bool> ReplayAsync(TwoRf3MembershipWave wave, NativeRemoteGraphRf3Seed seed, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            seed.Credential, async caller =>
            {
                await SqlRf3Protocol.EqualAsync(Upsert, await NativeRemoteGraphPublicCallers.UpsertAndReplayAsync(caller.Sdk, caller.Mcp, seed.Upsert, token));
                await SqlRf3Protocol.EqualAsync(Deliver, await NativeRemoteGraphPublicCallers.DeliverAndReplayAsync(caller.Sdk, caller.Mcp, seed.Deliver, token));
                await SqlRf3Protocol.EqualAsync(Delete, await NativeRemoteGraphPublicCallers.DeleteAndReplayAsync(caller.Sdk, caller.Mcp, seed.Delete, token));
                await NativeRemoteGraphRf3Assertions.IncomingAsync(caller, seed, false, token);
                return true;
            }, token);
}
