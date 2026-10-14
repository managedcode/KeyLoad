using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedFirstCleanupRf3Execution
{
    private const long InitialRevision = 0;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, string credential, global::KeyLoad.Client.KeyLoadClient sdk, EventFeedControlRequest original,
        RequestCqrsProbeFixture controls, Guid arm, Task<Result<EventFeedControlResult>> producer,
        IReadOnlyList<ReplicaSiloDiscovery> signed, CancellationToken token)
    {
        var marker = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.EventVectorFirstDispatchReserved,
            RequestCqrsProbeOutcome.Observed, signed, token).ConfigureAwait(false);
        var expiry = EventFeedReservedExpiryRf3Read.Read(controls, marker);
        await Assert.That(producer.IsCompleted).IsFalse();
        var concurrent = await sdk.OpenEventFeedAsync(original, token).ConfigureAwait(false);
        await Assert.That(concurrent.IsSuccess).IsFalse();
        await Assert.That(concurrent.Problem!.ErrorCode).IsEqualTo(ErrorCode.UnknownWriteOutcome.ToString());
        await Assert.That(producer.IsCompleted).IsFalse();
        var abandon = original with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Release,
            OriginalFirstIssuancePublicCommandId = original.CommandId
        };
        var abandoned = await EventFeedSettlementRf3Flow.RoutesAsync(wave, credential, abandon, token).ConfigureAwait(false);
        await EventFeedFirstCleanupRf3Assertions.ResultAsync(abandon, abandoned, released: false).ConfigureAwait(false);
        controls.WriteRelease(arm, marker.RequestId);
        var late = await producer.ConfigureAwait(false);
        await Assert.That(late.IsSuccess).IsFalse();
        await Assert.That(late.Problem!.ErrorCode).IsEqualTo(ErrorCode.RecoveryRequired.ToString());
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, arm, marker.RequestId,
            original.CommandId, signed, token).ConfigureAwait(false);
        await controls.RetireArmAsync(arm, token).ConfigureAwait(false);
        var premature = abandon with
        {
            CommandId = Guid.NewGuid(),
            ExpectedRevision = InitialRevision
        };
        await EventFeedFirstCleanupRf3Assertions.DeniedAsync(wave, credential, premature, token).ConfigureAwait(false);
        var remaining = expiry - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, TimeProvider.System, token).ConfigureAwait(false);
        }
        await Assert.That(TimeProvider.System.GetUtcNow() >= expiry).IsTrue();
        await EventFeedFirstCleanupRf3Assertions.DeniedAsync(wave, credential, premature, token).ConfigureAwait(false);
        var cleanup = premature with
        {
            CommandId = Guid.NewGuid()
        };
        var released = await EventFeedSettlementRf3Flow.RoutesAsync(wave, credential, cleanup, token).ConfigureAwait(false);
        await EventFeedFirstCleanupRf3Assertions.ResultAsync(cleanup, released, released: true).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(abandoned, await EventFeedSettlementRf3Flow.RoutesAsync(
            wave, credential, abandon, token).ConfigureAwait(false));
        _ = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(released, await EventFeedSettlementRf3Flow.RoutesAsync(
            wave, credential, cleanup, token).ConfigureAwait(false));
        await EventFeedFirstCleanupRf3Assertions.DeniedAsync(wave, credential, premature, token).ConfigureAwait(false);
    }
}
