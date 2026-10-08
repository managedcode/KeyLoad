using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Independent complete value oracle for the actual persisted A grant and native source fence.</summary>
internal static class ControlledPartitionMovementGrantAssertions
{
    private const int Version = 1;
    private const long InitialPolicyEpoch = 1;
    private const long GrantReplicaIndex = 12;
    private const long FenceReplicaIndex = 13;

    internal static async Task<PartitionMovePhaseResult> AuthorizedAsync(OperationResult outcome,
        PartitionMovePhaseResult prepared, ControlledPartitionMovementLoopbackCorpus corpus,
        DateTimeOffset originalExpiry)
    {
        await Assert.That(outcome.Error).IsNull();
        var actual = outcome.Get<PartitionMovePhaseResult>();
        var control = prepared.Control ?? throw new InvalidOperationException("The actual prepared control is absent.");
        var digest = Convert.ToHexStringLower(SHA256.HashData(ControlledPartitionMovementBodies.Control(control)));
        var resources = ControlledPartitionMovementResourceAssertions.Expected()
            .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToImmutableArray();
        var expectedGrant = new PartitionMovePhaseGrant(Version, ControlledPartitionMovementFenceRequest.GrantId,
            ControlledPartitionMovementFenceRequest.FenceCommandId, ControlledPartitionMovementPrepareRequest.MoveId,
            ControlledPartitionMovementCorpus.Partition, corpus.Control.Owner, corpus.Control.Owner,
            PhysicalShardCatalogFixture.RootPrincipalId, InitialPolicyEpoch, PartitionMovePeerStage.Fence,
            prepared.Journal.ControlIntentDigest, digest, originalExpiry, GrantReplicaIndex, null, resources);
        var expected = new PartitionMovePhaseResult(ControlledPartitionMovementPrepareRequest.MoveId,
            PartitionMovePeerStage.ControlAuthorize, new(ControlledPartitionMovementFenceRequest.GrantId,
                corpus.Control.Owner, GrantReplicaIndex, prepared.Journal.ControlIntentDigest),
            control, null, null, null, expectedGrant);
        var grant = actual.Grant ?? throw new InvalidOperationException("The actual logged A grant is absent.");
        await Assert.That(JsonDefaults.Serialize(actual with
        {
            Grant = grant with
            { Resources = grant.Resources.OrderBy(resource => resource.Name, StringComparer.Ordinal).ToImmutableArray() }
        })
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        return actual;
    }

    internal static async Task<PartitionMovePhaseResult> FencedAsync(OperationResult outcome,
        PartitionMovePhaseResult prepared, ControlledPartitionMovementLoopbackCorpus corpus)
    {
        await Assert.That(outcome.Error).IsNull();
        var control = prepared.Control ?? throw new InvalidOperationException("The actual prepared control is absent.");
        var fence = new PartitionMoveSourceFenceRecord(Version, ControlledPartitionMovementPrepareRequest.MoveId,
            ControlledPartitionMovementCorpus.Partition, corpus.Control.Owner, control.SourcePlacement,
            corpus.Destination.Owner, FenceReplicaIndex, prepared.Journal.ControlIntentDigest);
        var expected = new PartitionMovePhaseResult(ControlledPartitionMovementPrepareRequest.MoveId,
            PartitionMovePeerStage.Fence, new(ControlledPartitionMovementFenceRequest.FenceCommandId,
                corpus.Control.Owner, FenceReplicaIndex, prepared.Journal.ControlIntentDigest),
            control, fence, null, null);
        var actual = outcome.Get<PartitionMovePhaseResult>();
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        return actual;
    }
}
