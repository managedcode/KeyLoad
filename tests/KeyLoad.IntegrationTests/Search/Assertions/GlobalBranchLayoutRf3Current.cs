using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GlobalBranchLayoutRf3Current
{
    internal static KeyLoadClient LogicalSource(TwoRf3MembershipWave wave, DistributedSearchRf3Callers callers,
        GlobalBranchLayoutRf3State state)
        => callers.Sdk(state.Moved is null ? TwoRf3MembershipProtocol.Node1 : TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);

    internal static async Task ObserveAsync(TwoRf3MembershipWave wave, DistributedSearchRf3Callers callers,
        GlobalBranchLayoutRf3State state, Func<Task> operation, CancellationToken token)
    {
        var source = LogicalSource(wave, callers, state);
        var target = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
        var beforeSource = await McpCallerAssertions.SdkSuccessAsync(await source.OutboxStatusAsync(RemotePartitionQueryRf3Seed.Local, token));
        var beforeTarget = await McpCallerAssertions.SdkSuccessAsync(await target.OutboxStatusAsync(RemoteDocumentRf3Protocol.Partition, token));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => DistributedSearchRf3Documents.RequireAsync(source, target, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(beforeSource,
            await McpCallerAssertions.SdkSuccessAsync(await source.OutboxStatusAsync(RemotePartitionQueryRf3Seed.Local, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(beforeTarget,
            await McpCallerAssertions.SdkSuccessAsync(await target.OutboxStatusAsync(RemoteDocumentRf3Protocol.Partition, token))), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task<(OutboxStatus Source, OutboxStatus Target)> CaptureFaultAsync(
        TwoRf3MembershipWave wave, CancellationToken token)
    {
        var images = new List<(OutboxStatus Source, OutboxStatus Target)>();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var callers = new DistributedSearchRf3Callers(wave);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var source = callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
                var target = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
                images.Add((await McpCallerAssertions.SdkSuccessAsync(await source.OutboxStatusAsync(RemotePartitionQueryRf3Seed.Local, token)),
                    await McpCallerAssertions.SdkSuccessAsync(await target.OutboxStatusAsync(RemoteDocumentRf3Protocol.Partition, token))));
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return images.Single();
    }
    internal static async Task RequireFaultAsync(TwoRf3MembershipWave wave,
        (OutboxStatus Source, OutboxStatus Target) before, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var callers = new DistributedSearchRf3Callers(wave);
            var source = callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
            var target = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
            await ServerFailureObserver.ObserveAsync(() => DistributedSearchRf3Documents.RequireAsync(source, target, token), failures);
            await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(before.Source,
                await McpCallerAssertions.SdkSuccessAsync(await source.OutboxStatusAsync(RemotePartitionQueryRf3Seed.Local, token))), failures);
            await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(before.Target,
                await McpCallerAssertions.SdkSuccessAsync(await target.OutboxStatusAsync(RemoteDocumentRf3Protocol.Partition, token))), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
