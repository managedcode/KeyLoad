using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropRuntimeAssertions
{
    internal static T Value<T>(GrainOperationReply reply) where T : class
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? NativeDropRuntimeProtocol.MissingReply);
        }

        return GrainNativePayload.Read<GrainValue>(reply.Payload).Value as T
            ?? throw Errors.Fail(ErrorCode.Corruption, NativeDropRuntimeProtocol.MissingReply);
    }

    internal static async Task GenuineWorkSealAsync(ResourceRetirementStateV1 state, NativeDropRuntimeProfile profile)
    {
        ResourceRetirementWorkValidation.RequireBarrier(state, profile.Source.Database.Limits);
        await Assert.That(state.Control.Directory.Owners.Length).IsEqualTo(NativeDropRuntimeProtocol.OwnerGroups);
        await Assert.That(state.OwnerReceipts.Length).IsEqualTo(NativeDropRuntimeProtocol.OwnerGroups);
        await Assert.That(state.WorkOwnerReceipts.Length).IsEqualTo(NativeDropRuntimeProtocol.OwnerGroups);
        await Assert.That(state.OwnerCleanupReceipts.IsEmpty).IsTrue();
        foreach (var owner in state.WorkOwnerReceipts)
        {
            await Assert.That(owner.Evidence.Length).IsEqualTo(owner.Receiver.Owner.VoterIds.Length);
            var actual = profile.Hosts.Nodes.Where(node => node.Store.Identity.Incarnation == owner.Receiver.Owner.Incarnation)
                .Select(node => node.Store.Identity.NodeId).ToHashSet();
            await Assert.That(owner.Evidence.Select(item => item.Witness.StoreNodeId).ToHashSet().SetEquals(actual)).IsTrue();
        }
    }

    internal static async Task SameAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
