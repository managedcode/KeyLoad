using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedFirstCleanupRf3Flow
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest healthy, CancellationToken token)
    {
        var original = healthy with { CommandId = Guid.NewGuid(), MapId = Guid.NewGuid() };
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution());
        var signed = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave, token).ConfigureAwait(false);
        var controls = wave.QueryControls;
        var arm = controls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId, original.CommandId,
            null, RequestCqrsProbePhase.EventVectorFirstDispatchReserved, RequestCqrsProbeAction.Hold);
        var producer = sdk.OpenEventFeedAsync(original, token);
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await EventFeedFirstCleanupRf3Execution.RunAsync(wave, credential, sdk, original, controls, arm,
                    producer, signed, token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
            await ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(signed, cleanup.Token), failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(async () => { _ = await producer.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
