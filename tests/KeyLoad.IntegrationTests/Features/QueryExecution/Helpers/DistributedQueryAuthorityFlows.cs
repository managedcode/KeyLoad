using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedQueryAuthorityFlows
{
    private const string ScopeDenied = "The principal cannot perform this operation in this scope.";

    internal static async Task WarmAsync(TwoRf3MembershipWave wave, DistributedQueryAuthorityContext context,
        DistributedQueryAuthorityState state, bool useMcp, CancellationToken token)
    {
        await DistributedQueryAuthorityPages.HealthyAsync(context, state, token);
        await SourcePolicyAsync(wave, context, state, useMcp, token);
        await PrincipalPolicyAsync(context, state, token);
        await DistributedQueryAuthorityCurrent.ObserveAsync(context, state,
            () => RemotePartitionQueryRf3Cancellation.RunAsync(wave, context.Reader, context.Official, state.Seed, useMcp, token), token);
        await DistributedQueryAuthorityPages.HealthyAsync(context, state, token);
    }
    private static async Task SourcePolicyAsync(TwoRf3MembershipWave wave, DistributedQueryAuthorityContext context,
        DistributedQueryAuthorityState state, bool useMcp, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => DistributedQueryAuthorityHold.RunAsync(wave, context, state, useMcp, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await DistributedQueryAuthorityPolicy.ResourceAsync(context.Source, DistributedQueryAuthorityProtocol.RepairedSchema, false, token);
            state.SourceSchema = DistributedQueryAuthorityProtocol.RepairedSchema;
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => DistributedQueryAuthorityPages.HealthyAsync(context, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task PrincipalPolicyAsync(DistributedQueryAuthorityContext context,
        DistributedQueryAuthorityState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await DistributedQueryAuthorityPolicy.PrincipalAsync(context.Destination, state, DistributedQueryAuthorityProtocol.DeniedEpoch, false, token);
            await DeniedAsync(context, state, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => DistributedQueryAuthorityPolicy.PrincipalAsync(context.Destination, state,
            DistributedQueryAuthorityProtocol.RepairedEpoch, true, token), failures);
        await ServerFailureObserver.ObserveAsync(() => DistributedQueryAuthorityPages.HealthyAsync(context, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task DeniedAsync(DistributedQueryAuthorityContext context, DistributedQueryAuthorityState state,
        CancellationToken token)
    {
        await DistributedQueryAuthorityCurrent.ObserveAsync(context, state, async () =>
            await DistributedQueryAuthorityProblems.SdkAsync(await context.Reader.PartitionQueryAsync(RemotePartitionQueryRf3Seed.Request(), token),
                ErrorCode.PermissionDenied, ScopeDenied), token);
        await DistributedQueryAuthorityCurrent.ObserveAsync(context, state, async () =>
            await DistributedQueryAuthorityProblems.McpAsync(await context.Official.CallAsync(RemotePartitionQueryRf3Seed.Tool,
                RemotePartitionQueryRf3Seed.Request(), token), ErrorCode.PermissionDenied,
                ScopeDenied), token);
    }
}
