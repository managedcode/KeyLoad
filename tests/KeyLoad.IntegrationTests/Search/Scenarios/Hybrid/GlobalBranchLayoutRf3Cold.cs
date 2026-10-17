using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GlobalBranchLayoutRf3Cold
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, GlobalBranchLayoutRf3State state, GlobalBranchLayoutRf3FaultEvidence evidence, CancellationToken token)
    {
        var failures = new List<Exception>();
        for (var cold = 0; cold < 2; cold++)
        {
            await ServerFailureObserver.ObserveAsync(() => CycleAsync(wave, state, token), failures);
            await ServerFailureObserver.ObserveAsync(() => evidence.RequireAsync(token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task CycleAsync(TwoRf3MembershipWave wave, GlobalBranchLayoutRf3State state, CancellationToken token)
    {
        var before = await DistributedSearchRf3RawCut.StopAndReadAsync(wave, token);
        var nativeCuts = new[]
        {
            before.Take(TwoRf3MembershipProtocol.MembersPerGroup).Select(Position).ToArray(),
            before.Skip(TwoRf3MembershipProtocol.MembersPerGroup).Select(Position).ToArray()
        };
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await wave.RestartJoinedAsync(token);
            await using var callers = new DistributedSearchRf3Callers(wave);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token);
                await GlobalBranchLayoutRf3Calls.HealthyAsync(wave, callers, state, 7L, nativeCuts, token);
                await GlobalBranchLayoutRf3Receipts.RequireAsync(wave, callers, state, token);
            }, failures);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var after = await DistributedSearchRf3RawCut.StopAndReadAsync(wave, token);
            await SqlRf3Protocol.EqualAsync(before, after);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static long Position(string[] actual)
        => long.Parse(actual[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
}
