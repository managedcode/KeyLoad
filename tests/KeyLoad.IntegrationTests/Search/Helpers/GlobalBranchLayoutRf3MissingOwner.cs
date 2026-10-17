using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GlobalBranchLayoutRf3MissingOwner
{
    private static readonly string[] TargetNodes = [TwoRf3MembershipProtocol.Node4, TwoRf3MembershipProtocol.Node5, TwoRf3MembershipProtocol.Node6];
    internal static async Task RunAsync(TwoRf3MembershipWave wave, GlobalBranchLayoutRf3State state,
        bool official, GlobalBranchLayoutRf3FaultEvidence evidence, CancellationToken token)
    {
        var scenario = evidence.Begin(state.TargetOwner.Incarnation, official);
        var before = await GlobalBranchLayoutRf3Current.CaptureFaultAsync(wave, token);
        var failures = new List<Exception>();
        var stopped = new List<string>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            foreach (var node in TargetNodes)
            {
                await wave.RemoteRuntime.KillAsync(node, scenario, token);
                stopped.Add(node);
            }
            await using var callers = new DistributedSearchRf3Callers(wave);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var reader = callers.Sdk(TwoRf3MembershipProtocol.Node2, state.Seed.Original.Destination.Secret);
                var mcp = await callers.OfficialAsync(TwoRf3MembershipProtocol.Node3, state.Seed.Original.Destination.Secret, token);
                var request = DistributedSearchRf3Seed.Request();
                if (official)
                {
                    var original = mcp.CallAsync(DistributedSearchProtocol.Tool, request, token);
                    var result = await original;
                    await McpCallerAssertions.ErrorAsync(result, ErrorCode.OwnershipLost, dispatched: true);
                    await GlobalBranchLayoutRf3Problems.OfficialAsync(result, ErrorCode.OwnershipLost, GlobalBranchLayoutRf3State.Generic);
                }
                else
                {
                    var original = reader.DistributedSearchAsync(request, token);
                    var result = await original;
                    await Assert.That(result.IsSuccess).IsFalse();
                    await Assert.That(result.Value).IsNull();
                    await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.OwnershipLost, GlobalBranchLayoutRf3State.MissingDetail), result.Problem);
                }
            }, failures);
        }, failures);
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.BeginRestartAsync(node, token), failures);
        }
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.RestartAsync(node, token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => evidence.CaptureAsync(stopped, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GlobalBranchLayoutRf3Current.RequireFaultAsync(wave, before, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
