using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ResourceExecution.EarlyAdmissionQualificationProtocol;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class EarlyAdmissionQualificationFlow
{
    private const long MaximumBytes = 130_000;
    private const long OtherBytes = 0;
    private const int BodyBytes = 8_192;
    private const int ControlBodyBytes = 1_024;
    private const long ControlBytes = 20_000;
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new HttpAdmissionLimits
            {
                MaxReservedBytes = MaximumBytes,
                HeavyReadReservedBytes = HeavyBytes,
                OtherReservedBytes = OtherBytes,
                MaxBodyBytes = BodyBytes,
                MaxControlBodyBytes = ControlBodyBytes,
                ReservedControlBytes = ControlBytes
            });
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<EarlyAdmissionQualificationTests>(
                    nameof(EarlyAdmissionQualificationTests.HeldWireRefusesMalformedSqlBeforeParseThenFreshPolicyAndTwoCold));
                EarlyAdmissionQualificationState state;
                using (var phase = McpCallerDeadline.Create())
                {
                    state = await RootAsync(fixture, async root =>
                    {
                        var current = await EarlyAdmissionQualificationSetup.RunAsync(fixture, root, phase.Token);
                        await ReaderAsync(fixture, current, async reader =>
                        {
                            await EarlyAdmissionQualificationHeld.RunAsync(fixture, root, reader, current, phase.Token);
                            await HealthyAsync(root, reader, current, phase.Token);
                            await EarlyAdmissionQualificationSetup.PolicyAsync(root, current, false, DeniedEpoch, phase.Token);
                            await EarlyAdmissionQualificationReads.WaveAsync(root, reader, current,
                                false, ErrorCode.PermissionDenied, false, phase.Token);
                            await EarlyAdmissionQualificationSetup.PolicyAsync(root, current, true, RepairedEpoch, phase.Token);
                            await HealthyAsync(root, reader, current, phase.Token);
                        }, phase.Token);
                        return current;
                    }, phase.Token);
                }
                for (var cold = 0; cold < ColdCount; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var phase = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, phase.Token);
                        _ = await RootAsync(fixture, async root =>
                        {
                            await ReaderAsync(fixture, state, reader => HealthyAsync(root, reader, state, phase.Token), phase.Token);
                            return true;
                        }, phase.Token);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task HealthyAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        EarlyAdmissionQualificationState state, CancellationToken token)
    {
        await EarlyAdmissionQualificationReads.WaveAsync(root, reader, state, true, ErrorCode.Validation, false, token);
        await EarlyAdmissionQualificationReads.WaveAsync(root, reader, state, false, null, false, token);
        await EarlyAdmissionQualificationReplay.RunAsync(root, state, token);
    }
    private static Task<T> RootAsync<T>(ClusterFixture fixture, Func<RequestCqrsRf3Callers, Task<T>> operation,
        CancellationToken token) => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);
    private static Task<bool> ReaderAsync(ClusterFixture fixture, EarlyAdmissionQualificationState state,
        Func<RequestCqrsRf3Callers, Task> operation, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Identity.Secret,
            async reader => { await operation(reader); return true; }, token);
}
