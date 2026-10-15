using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GlobalBranchLayoutRf3Calls
{
    internal static async Task HealthyAsync(TwoRf3MembershipWave wave, DistributedSearchRf3Callers callers,
        GlobalBranchLayoutRf3State state, long epoch, long[][]? nativeCuts, CancellationToken token)
    {
        var reader = callers.Sdk(TwoRf3MembershipProtocol.Node2, state.Seed.Original.Destination.Secret);
        var official = await callers.OfficialAsync(TwoRf3MembershipProtocol.Node3, state.Seed.Original.Destination.Secret, token);
        var request = DistributedSearchRf3Seed.Request();
        for (var route = 0; route < 4; route++)
        {
            await GlobalBranchLayoutRf3Current.ObserveAsync(wave, callers, state, async () =>
            {
                var source = GlobalBranchLayoutRf3Current.LogicalSource(wave, callers, state);
                var target = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
                var beforeSource = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
                var beforeTarget = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
                var page = await OneAsync(reader, official, request, route, token);
                var afterSource = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
                var afterTarget = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
                await Assert.That(afterSource.Applied).IsEqualTo(beforeSource.Applied);
                await Assert.That(afterTarget.Applied).IsEqualTo(beforeTarget.Applied);
                await GlobalBranchLayoutRf3Oracle.PageAsync(page, state, epoch, nativeCuts);
            }, token);
        }
    }
    private static async Task<DistributedSearchPageV1> OneAsync(KeyLoadClient reader, McpOfficialClient official,
        DistributedSearchRequestV1 request, int route, CancellationToken token)
    {
        var sql = SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition, DistributedSearchProtocol.Tool, request);
        return route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await reader.DistributedSearchAsync(request, token)),
            1 => (await McpCallerAssertions.SuccessAsync<DistributedSearchPageV1>(await official.CallAsync(
                DistributedSearchProtocol.Tool, request, token))).Value,
            2 => await SqlRf3Protocol.SdkAsync<DistributedSearchPageV1>(reader, sql, token),
            _ => await SqlRf3Protocol.McpAsync<DistributedSearchPageV1>(official, sql, token)
        };
    }
    internal static async Task DeniedAsync(TwoRf3MembershipWave wave, DistributedSearchRf3Callers callers,
        GlobalBranchLayoutRf3State state, CancellationToken token)
    {
        var reader = callers.Sdk(TwoRf3MembershipProtocol.Node2, state.Seed.Original.Destination.Secret);
        var official = await callers.OfficialAsync(TwoRf3MembershipProtocol.Node3, state.Seed.Original.Destination.Secret, token);
        await GlobalBranchLayoutRf3Current.ObserveAsync(wave, callers, state, async () =>
        {
            var result = await reader.DistributedSearchAsync(DistributedSearchRf3Seed.Request(), token);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Value).IsNull();
            await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, GlobalBranchLayoutRf3State.ScopeDenied), result.Problem);
        }, token);
        await GlobalBranchLayoutRf3Current.ObserveAsync(wave, callers, state, async () =>
        {
            var reply = await official.CallAsync(DistributedSearchProtocol.Tool, DistributedSearchRf3Seed.Request(), token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
            await GlobalBranchLayoutRf3Problems.OfficialAsync(reply, ErrorCode.PermissionDenied, GlobalBranchLayoutRf3State.ScopeDenied);
        }, token);
    }
}
