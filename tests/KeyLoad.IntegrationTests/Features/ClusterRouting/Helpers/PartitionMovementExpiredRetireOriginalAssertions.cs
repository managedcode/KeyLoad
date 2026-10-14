using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementExpiredRetireOriginalAssertions
{
    private const string MissingNativePhase = "The original native expired Retire phase is absent.";

    internal static PartitionMoveParentPhase RequirePending(PartitionMovementPublicParentRf3NativeCut[] cuts, Guid originalId)
    {
        var pending = cuts.Where(cut => cut.Pending?.OriginalPhaseCommandId == originalId).Select(cut => cut.Pending!).ToArray();
        if (pending.Length != TwoRf3MembershipProtocol.MembersPerGroup || pending.Any(value => value.Stage != PartitionMovePeerStage.Retire))
        {
            throw new InvalidOperationException(MissingNativePhase);
        }
        var bytes = NativeSerialization.Serialize(pending.First());
        if (pending.Any(value => !NativeSerialization.Serialize(value).AsSpan().SequenceEqual(bytes)))
        {
            throw new InvalidOperationException(MissingNativePhase);
        }
        return pending.First();
    }

    internal static async Task RequireOriginalAsync(PartitionMoveParentPhase original, PartitionMoveParentPhase actual)
    {
        await SqlRf3Protocol.EqualAsync(original.OriginalPhase, actual.OriginalPhase);
        await SqlRf3Protocol.EqualAsync(original.OriginalGrant, actual.OriginalGrant);
        await SqlRf3Protocol.EqualAsync(original.OriginalAuthorization, actual.OriginalAuthorization);
        await PartitionMovementHistoricalPacketAssertions.ExactAsync(original, actual);
        await Assert.That(actual.OriginalRequestNonce).IsEqualTo(original.OriginalRequestNonce);
        await Assert.That(actual.OriginalExpiresAt).IsEqualTo(original.OriginalExpiresAt);
        await Assert.That(actual.OriginalIssuancePolicyEpoch).IsEqualTo(original.OriginalIssuancePolicyEpoch);
        await Assert.That(actual.CleanupGeneration).IsEqualTo(original.CleanupGeneration);
        await Assert.That(actual.OriginalResult).IsNull();
    }

}
