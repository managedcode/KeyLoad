using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ResourceExecution.EarlyAdmissionQualificationProtocol;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class SlowBodyAdmissionReturnFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new HttpAdmissionLimits
            {
                MaxReservedBytes = SlowBodyAdmissionReturnProtocol.MaximumBytes,
                HeavyReadReservedBytes = HeavyBytes,
                OtherReservedBytes = 0,
                MaxBodyBytes = SlowBodyAdmissionReturnProtocol.BodyBytes,
                MaxControlBodyBytes = SlowBodyAdmissionReturnProtocol.ControlBodyBytes,
                ReservedControlBytes = SlowBodyAdmissionReturnProtocol.ControlBytes
            });
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<SlowBodyAdmissionReturnTests>(nameof(SlowBodyAdmissionReturnTests.CanceledAndDisconnectedSlowBodiesReturnCapacityThenBothCallersAndTwoCold));
                EarlyAdmissionQualificationState state;
                using (var phase = McpCallerDeadline.Create())
                {
                    state = await RootAsync(fixture, async root =>
                    {
                        var seeded = await EarlyAdmissionQualificationSetup.RunAsync(fixture, root, phase.Token);
                        foreach (var mode in Enum.GetValues<SlowBodyAdmissionReturnMode>())
                        {
                            await EarlyAdmissionQualificationCurrent.FullAsync(root, seeded, phase.Token);
                            await ServerFailureObserver.ObserveAsync(() => SlowBodyAdmissionReturnAttempt.RunAsync(fixture, root, seeded, mode, phase.Token), failures);
                            await ServerFailureObserver.ObserveAsync(() => HealthyAsync(fixture, root, seeded, phase.Token), failures);
                        }
                        return seeded;
                    }, phase.Token);
                }
                for (var cold = 0; cold < ColdCount; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var phase = McpCallerDeadline.Create();
                        await ModelSqlClusterCold.RunAsync(fixture, phase.Token);
                        _ = await RootAsync(fixture, async root =>
                        {
                            await HealthyAsync(fixture, root, state, phase.Token);
                            return true;
                        }, phase.Token);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static Task<bool> HealthyAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        EarlyAdmissionQualificationState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Identity.Secret, async reader =>
        {
            await EarlyAdmissionQualificationReads.WaveAsync(root, reader, state, true, ErrorCode.Validation, false, token);
            await EarlyAdmissionQualificationReads.WaveAsync(root, reader, state, false, null, false, token);
            await EarlyAdmissionQualificationReplay.RunAsync(root, state, token);
            return true;
        }, token);
    private static Task<T> RootAsync<T>(ClusterFixture fixture, Func<RequestCqrsClusterCallers, Task<T>> operation, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, operation, token);
}
