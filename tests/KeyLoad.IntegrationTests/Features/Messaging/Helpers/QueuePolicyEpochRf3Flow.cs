using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.GraphTraversal;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueuePolicyEpochRf3Flow
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token);
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token);
        var administrator = await PartitionMovementPublicParentRf3Administrator.PersistAsync(wave, token);
        var seed = await QueuePolicyEpochRf3Seed.CreateAsync(wave, administrator, token);
        var original = await QueuePolicyEpochRf3Delivery.ExecuteAsync(wave, seed,
            QueuePolicyEpochRf3Seed.Message, seed.Principal.PolicyEpoch, token);
        _ = await QueuePolicyEpochRf3Assertions.OriginalAsync(wave, seed, token);
        var revoked = await QueuePolicyEpochRf3Routes.PolicyAsync(wave, administrator, seed, seed.Principal,
            Capability.QueueConsume | Capability.QueueInspect, token);
        await Assert.That(revoked.PolicyEpoch).IsEqualTo(2L);
        var denied = original.Select(value => new QueuePolicyEpochRf3Known(
            value.Command with
            {
                CommandId = Guid.NewGuid()
            }, revoked.PolicyEpoch, null, ErrorCode.PermissionDenied)).ToArray();
        var forbidden = original.Concat(denied).Select(value => value.Command).ToArray();
        _ = await QueuePolicyEpochRf3Routes.RefuseAsync(wave, seed, forbidden, token);
        var known = original.Concat(denied).ToArray();
        await ColdAsync(wave, directory, seed, known, forbidden, [], token);
        await wave.RestartJoinedAsync(token);
        var restored = await QueuePolicyEpochRf3Routes.PolicyAsync(wave, administrator, seed, revoked,
            QueuePolicyEpochRf3Seed.Granted, token);
        await Assert.That(restored.PolicyEpoch).IsEqualTo(3L);
        _ = await QueuePolicyEpochRf3Routes.RefuseAsync(wave, seed, forbidden, token);
        _ = await QueuePolicyEpochRf3Delivery.PublishHealthyAsync(wave, administrator, seed, token);
        var healthy = await QueuePolicyEpochRf3Delivery.ExecuteAsync(wave, seed,
            QueuePolicyEpochRf3Seed.Healthy, restored.PolicyEpoch, token);
        await ColdAsync(wave, directory, seed, known.Concat(healthy).ToArray(), forbidden, healthy, token);
    }

    private static async Task ColdAsync(TwoRf3MembershipWave wave, PhysicalOwnerDirectoryV1 directory,
        QueuePolicyEpochRf3Seed seed, QueuePolicyEpochRf3Known[] known, DeliveryCommand[] forbidden,
        QueuePolicyEpochRf3Known[] healthy, CancellationToken token)
    {
        var before = await NativeRemoteGraphRf3Cuts.CaptureAsync(wave, directory, token);
        await QueuePolicyEpochRf3Assertions.CountersAsync(before, seed, token);
        await wave.RestartJoinedAsync(token);
        _ = await QueuePolicyEpochRf3Routes.RefuseAsync(wave, seed, forbidden, token);
        foreach (var item in healthy)
        {
            await SqlRf3Protocol.EqualAsync(item.Receipt,
                await QueuePolicyEpochRf3Routes.CompleteAsync(wave, seed, item.Command, token));
        }
        _ = await QueuePolicyEpochRf3Assertions.OriginalAsync(wave, seed, token);
        var after = await NativeRemoteGraphRf3Cuts.CaptureAsync(wave, directory, token);
        await QueuePolicyEpochRf3Assertions.CountersAsync(after, seed, token);
        await QueuePolicyEpochRf3ColdAssertions.RequireAsync(before, after, seed, known, token);
    }
}
