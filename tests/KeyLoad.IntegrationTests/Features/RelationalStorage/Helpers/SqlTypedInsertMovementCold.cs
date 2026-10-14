using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class SqlTypedInsertMovementCold
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, SqlTypedInsertMovementTls tls,
        PartitionMovementPublicParentRf3Seed seed, SqlTypedInsertMovementState state, CancellationToken token)
    {
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await seed.ReconnectAsync(wave, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => SqlTypedInsertMovementConservation.ObserveAsync(seed, async () =>
        {
            await SqlTypedInsertMovementReplay.RunAsync(wave, tls, state, token);
            if (state.Healthy is not null)
            {
                await SqlTypedInsertMovementHealthy.ReplayAsync(wave, tls, seed, state, token);
            }
            await seed.VerifyAsync(token);
        }, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
            await SqlRf3Protocol.EqualAsync(before, after);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token), failures);
        await ServerFailureObserver.ObserveAsync(() => seed.ReconnectAsync(wave, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
