using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardPartitionMovementRf3JournalAssertions
{
    private const int FirstEntry = 0;
    private const int SecondEntry = 1;
    private const long PublicationStep = 1;
    private const long UnappliedPosition = 0;

    internal static async Task ProofAsync(RequestCqrsMovementJournalRecord proof,
        PartitionMovementPublicParentRf3Seed seed, Guid originalPhase,
        PartitionMovePeerStage expectedStage = PartitionMovePeerStage.ControlPrepare)
    {
        await Assert.That(proof.Error).IsNull();
        await Assert.That(proof.CommandId).IsEqualTo(originalPhase);
        await Assert.That(proof.MoveId).IsEqualTo(seed.FirstRequest.MoveId);
        await Assert.That(proof.Partition).IsEqualTo(seed.Partition);
        await Assert.That(proof.PrincipalId).IsEqualTo(PartitionMovementPublicParentRf3Administrator.PrincipalId);
        await Assert.That(proof.Stage).IsEqualTo(expectedStage);
        await Assert.That(proof.Journal).IsNotNull();
        await SqlRf3Protocol.EqualAsync(seed.Directory.ControlOwner, proof.Journal!.PhysicalOwner);
        await Assert.That(proof.Journal.AppliedPosition).IsGreaterThan(0);
    }

    internal static async Task ColdAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, RequestCqrsMovementJournalRecord proof, string node,
        PartitionMovePeerStage expectedStage = PartitionMovePeerStage.ControlPrepare)
    {
        var actual = ReadPhase(wave, node, proof);
        await Assert.That(actual.Error).IsNull();
        var phase = actual.Get<PartitionMovePhaseResult>();
        await Assert.That(phase.Stage).IsEqualTo(expectedStage);
        await Assert.That(phase.MoveId).IsEqualTo(proof.MoveId);
        await SqlRf3Protocol.EqualAsync(proof.Journal!, phase.Journal);
        await Assert.That(phase.Control).IsNotNull();
        await Assert.That(phase.Control!.Partition).IsEqualTo(seeds[FirstEntry].Partition);
        await Assert.That(phase.Control.Phase).IsEqualTo(ExpectedPhase(expectedStage));
        await Assert.That(phase.Control.PrincipalId).IsEqualTo(proof.PrincipalId);
        await SqlRf3Protocol.EqualAsync(seeds[FirstEntry].OriginalPlacement, phase.Control.SourcePlacement);
        var target = seeds[FirstEntry].Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
            == seeds[FirstEntry].FirstRequest.DestinationPhysicalShardId).Owner;
        await SqlRf3Protocol.EqualAsync(target, phase.Control.DestinationOwner);
        await RequireLaterPhaseAsync(phase, seeds[FirstEntry], target);
        foreach (var owner in TwoRf3MembershipProtocol.Nodes)
        {
            var untouched = PartitionMovementPublicParentRf3Cut.ReadStopped(wave, owner,
                seeds[SecondEntry].FirstRequest, Guid.Empty);
            await Assert.That(untouched.Header).IsNull();
            await Assert.That(untouched.Pending).IsNull();
            await Assert.That(untouched.LastIssued).IsNull();
            await Assert.That(untouched.SettledStagePages.Length).IsEqualTo(0);
        }
    }

    private static PartitionMovePhase ExpectedPhase(PartitionMovePeerStage stage)
        => stage switch
        {
            PartitionMovePeerStage.ControlPrepare => PartitionMovePhase.Prepared,
            PartitionMovePeerStage.ControlAcceptFence => PartitionMovePhase.Fenced,
            PartitionMovePeerStage.ControlFinalize => PartitionMovePhase.Published,
            _ => throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch)
        };

    private static async Task RequireLaterPhaseAsync(PartitionMovePhaseResult phase,
        PartitionMovementPublicParentRf3Seed seed, PhysicalShardRecord target)
    {
        if (phase.Stage == PartitionMovePeerStage.ControlPrepare)
        {
            return;
        }
        await Assert.That(phase.Control!.SourceCut).IsGreaterThan(UnappliedPosition);
        if (phase.Stage == PartitionMovePeerStage.ControlAcceptFence)
        {
            await Assert.That(phase.Fence).IsNotNull();
            await Assert.That(phase.Control.SourceCut).IsEqualTo(phase.Fence!.SourceCut);
            await Assert.That(phase.Fence.MoveId).IsEqualTo(seed.FirstRequest.MoveId);
            await Assert.That(phase.Fence.Partition).IsEqualTo(seed.Partition);
            await SqlRf3Protocol.EqualAsync(seed.OriginalPlacement, phase.Fence.SourcePlacement);
            await SqlRf3Protocol.EqualAsync(target, phase.Fence.DestinationOwner);
            await Assert.That(phase.Fence.ControlIntentDigest).IsEqualTo(phase.Journal.ControlIntentDigest);
            return;
        }
        await Assert.That(phase.Control.InstalledReceipt).IsNotNull();
        await Assert.That(phase.Control.PublishedPlacement).IsNotNull();
        await Assert.That(phase.Control.InstalledReceipt!.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(phase.Control.InstalledReceipt.Incarnation).IsEqualTo(target.Incarnation);
        var expected = new AtomicPartitionPlacementV1(AtomicPartitionPlacementProtocol.CurrentVersion,
            seed.Partition, target.PhysicalShardId, checked(seed.OriginalPlacement.DirectoryRevision + PublicationStep),
            target.Incarnation, target.VoterIds,
            checked(Math.Max(seed.OriginalPlacement.PlacementEpoch, target.PlacementEpoch) + PublicationStep));
        await SqlRf3Protocol.EqualAsync(expected, phase.Control.PublishedPlacement);
        await SqlRf3Protocol.EqualAsync(expected, phase.PublishedPlacement);
    }

    internal static OperationResult ReadPhase(TwoRf3MembershipWave wave, string node,
        RequestCqrsMovementJournalRecord proof)
    {
        var failures = new List<Exception>();
        OperationResult? result = null;
        try
        {
            using var store = new ZoneTreeStore(new(Path.Combine(wave.OwnedDataRoot, node, "database")),
                IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
            ServerFailureObserver.Observe(() => result = store.Read(view =>
            {
                if (store.Position < proof.Journal!.AppliedPosition)
                {
                    throw new InvalidOperationException("The stopped native journal precedes its observed Prepare.");
                }
                return CommandOutcomeKeyResolver.Select(view, proof.PrincipalId, proof.CommandId,
                    new(CommandOutcomeScopeKind.Partition, proof.Partition)).Outcome?.Result
                    ?? throw new InvalidOperationException("The original committed scoped Prepare outcome is absent.");
            }), failures);
        }
        catch (Exception cleanup) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(cleanup) is null)
        {
            failures.Add(cleanup);
        }
        catch (Exception cleanup) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(cleanup) is not null)
        {
            failures.Add(cleanup);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The actual scoped Prepare outcome is absent.");
    }
}
