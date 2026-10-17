using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresCompositionRf3Flow
{
    internal static async Task<PostgresCompositionRf3State> WarmAsync(ClusterFixture fixture,
        KeyLoadClient root, PostgresRf3State original, CancellationToken token)
    {
        var seed = await PostgresCompositionRf3Provisioning.CreateAsync(root, original.Dialect, token);
        return await RunOwnedAsync(fixture, seed.Identity, async (callers, wire) =>
        {
            await PostgresCompositionRf3Operations.RefusedAsync(callers, wire, seed.Denied,
                ErrorCode.PermissionDenied, PostgresRf3Protocol.PermissionSqlState, token);
            await SqlRf3Protocol.EqualAsync(seed.Before, await PostgresCompositionRf3Models.ReadAsync(root, token));
            var principal = await PostgresCompositionRf3Operations.RepairAsync(root, seed.Principal, token);
            seed = seed with
            {
                Principal = principal
            };
            await PostgresCompositionRf3Operations.RefusedAsync(callers, wire, seed.Denied,
                ErrorCode.PermissionDenied, PostgresRf3Protocol.PermissionSqlState, token);
            await SqlRf3Protocol.EqualAsync(seed.Before, await PostgresCompositionRf3Models.ReadAsync(root, token));
            var forward = seed.Denied with
            {
                CommandId = Guid.NewGuid()
            };
            var receipt = await PostgresCompositionRf3Operations.ApplyAsync(callers, wire, forward, token);
            var afterForward = await PostgresCompositionRf3Models.ReadAsync(root, token);
            await PostgresCompositionRf3Models.LiteralAsync(afterForward, reverse: false);
            await SqlRf3Protocol.EqualAsync(seed.Before.Source, afterForward.Source);
            var changed = forward with
            {
                Mutations = [((QueueToGraph)forward.Mutations[0]) with { EdgeIdPrefix = ChangedPrefix }]
            };
            await PostgresCompositionRf3Operations.RefusedAsync(callers, wire, changed,
                ErrorCode.Conflict, PostgresRf3Protocol.ConflictSqlState, token);
            await SqlRf3Protocol.EqualAsync(afterForward, await PostgresCompositionRf3Models.ReadAsync(root, token));
            await PostgresCompositionRf3Operations.ReplayAsync(callers, wire, forward, receipt, token);
            var reverse = new CommandRequest(Guid.NewGuid(), PostgresRf3State.Partition,
                [new GraphToQueueMutation(PostgresCompositionRf3Protocol.TargetQueue, PostgresCompositionRf3Protocol.Graph,
                    PostgresRf3State.Left, PostgresCompositionRf3Protocol.TargetPrefix)]);
            var reverseReceipt = await PostgresCompositionRf3Operations.ApplyAsync(callers, wire, reverse, token);
            var image = await PostgresCompositionRf3Models.ReadAsync(root, token);
            await PostgresCompositionRf3Models.LiteralAsync(image, reverse: true);
            await SqlRf3Protocol.EqualAsync(seed.Before.Source, image.Source);
            return new PostgresCompositionRf3State(seed, forward, receipt, reverse, reverseReceipt, image);
        }, token);
    }

    internal static Task<bool> ColdAsync(ClusterFixture fixture, PostgresCompositionRf3State state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await SqlRf3Protocol.EqualAsync(state.Seed.SeedReceipt, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.CommitAsync(state.Seed.Seed, token)));
            return await RunOwnedAsync(fixture, state.Seed.Identity, async (callers, wire) =>
            {
                await PostgresCompositionRf3Operations.RefusedAsync(callers, wire, state.Seed.Denied,
                    ErrorCode.PermissionDenied, PostgresRf3Protocol.PermissionSqlState, token);
                await PostgresCompositionRf3Operations.ReplayAsync(callers, wire, state.Forward, state.ForwardReceipt, token);
                await PostgresCompositionRf3Operations.ReplayAsync(callers, wire, state.Reverse, state.ReverseReceipt, token);
                var image = await PostgresCompositionRf3Models.ReadAsync(callers.Sdk, token);
                await PostgresCompositionRf3Models.LiteralAsync(image, reverse: true);
                await SqlRf3Protocol.EqualAsync(state.Image, image);
                return true;
            }, token);
        }, token);

    private static Task<T> RunOwnedAsync<T>(ClusterFixture fixture, McpPersistedIdentity identity,
        Func<RequestCqrsRf3Callers, PostgresRf3Caller, Task<T>> operation, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret, async callers =>
        {
            T? result = default;
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    identity.Principal.Id, identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await ServerFailureObserver.ObserveAsync(async () => { result = await operation(callers, wire); }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            return result!;
        }, token);

    private const string ChangedPrefix = "native-conflict-";
}
