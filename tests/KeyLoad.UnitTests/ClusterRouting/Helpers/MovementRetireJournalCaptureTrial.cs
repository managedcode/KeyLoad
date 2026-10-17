using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class MovementRetireJournalCaptureTrial
{
    private const string SiloAddress = "S127.0.0.1:11111:1";
    private const string GuidFormat = "N";
    private const int InvalidBatch = 0;
    internal static async Task RequireAsync(ControlledPartitionMovementNode source)
    {
        var commandId = ControlledPartitionMovementRetirePhaseIds.Command(PartitionMoveCleanupFamilies.All.Length);
        var partition = ControlledPartitionMovementCorpus.Partition;
        var actual = source.Store.Read(view => CommandOutcomeKeyResolver.Select(view,
            PhysicalShardCatalogFixture.RootPrincipalId, commandId,
            new(CommandOutcomeScopeKind.Partition, partition)).Outcome!.Result);
        var phase = actual.Get<PartitionMovePhaseResult>();
        await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.Retire);
        await Assert.That(phase.Control).IsNull();
        await Assert.That(phase.Cleanup).IsNotNull();
        await Assert.That(phase.Cleanup!.Completion).IsNotNull();
        var claim = Claim(partition, commandId);
        var voter = phase.Journal.PhysicalOwner.VoterIds.First();
        var record = RequestCqrsMovementJournalCapture.Capture(claim, actual, voter, SiloAddress);
        RequestCqrsMovementJournalInventory.Require(record);
        RequestCqrsMovementJournalInventory.RequireArm(record, claim.Arm.Record);
        var marker = new RequestCqrsProbeMarkerRecord(RequestCqrsProbeProtocol.Version, RequestCqrsProbeProtocol.MarkerKind,
            record.SessionId, record.ArmId, record.RequestId, record.CommandId, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Observed, voter, SiloAddress);
        RequestCqrsMovementJournalInventory.RequireMarker(record, marker);
        RequestCqrsMovementJournalInventory.RequireInventory([record], [marker], [claim.Arm], _ => null);
        var json = new RequestCqrsProbeJson(Options.Create(new RequestProbeExecutionOptions()));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, json.MovementJournalTypeInfo);
        var decoded = json.Read(bytes, RequestCqrsMovementJournalInventory.Fields, json.MovementJournalTypeInfo);
        await Assert.That(JsonDefaults.Serialize(record).SequenceEqual(JsonDefaults.Serialize(decoded))).IsTrue();
        await Assert.That(record.MoveId).IsEqualTo(phase.MoveId);
        await Assert.That(JsonDefaults.Serialize(record.Journal).SequenceEqual(JsonDefaults.Serialize(phase.Journal))).IsTrue();
        await Assert.That(record.Partition).IsEqualTo(partition);
        await RejectAsync(claim, actual, phase, voter);
    }

    private static RequestCqrsProbeClaim Claim(PartitionRef partition, Guid commandId)
    {
        var arm = new RequestCqrsProbeArmRecord(RequestCqrsProbeProtocol.Version, RequestCqrsProbeProtocol.ArmKind,
            Guid.NewGuid().ToString(GuidFormat), Guid.NewGuid(), PhysicalShardCatalogFixture.RootPrincipalId,
            commandId, null, RequestCqrsProbePhase.SubmitReturned, RequestCqrsProbeAction.Hold,
            new(partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey));
        return new(new(arm, []), new GrainRequestProbeIdentity(Guid.NewGuid(), commandId,
            arm.PrincipalId, null, OperationKind.PartitionMovementPhase))
        {
            ConnectionId = Guid.NewGuid()
        };
    }

    private static PartitionMovePhaseResult WrongCommand(PartitionMovePhaseResult phase)
    {
        var changed = phase.Journal with
        {
            CommandId = Guid.NewGuid()
        };
        return phase with
        {
            Journal = changed,
            Cleanup = phase.Cleanup! with
            {
                Completion = changed
            }
        };
    }

    private static async Task RejectAsync(RequestCqrsProbeClaim claim, OperationResult original,
        PartitionMovePhaseResult phase, string voter)
    {
        var cleanup = phase.Cleanup!;
        PartitionMovePhaseResult[] invalid =
        [
            phase with { Cleanup = null },
            phase with { Stage = PartitionMovePeerStage.Abort },
            phase with { MoveId = Guid.Empty },
            phase with { Cleanup = cleanup with { MoveId = Guid.NewGuid() } },
            phase with { Cleanup = cleanup with { Partition = cleanup.Partition with { PartitionKey = Guid.NewGuid().ToString(GuidFormat) } } },
            phase with { Cleanup = cleanup with { Role = PartitionMoveCleanupRole.Target } },
            phase with { Cleanup = cleanup with { NextBatch = InvalidBatch } },
            phase with { Cleanup = cleanup with { Completion = cleanup.Completion! with { CommandId = Guid.NewGuid() } } },
            phase with { Journal = phase.Journal with { CommandId = Guid.NewGuid() } },
            WrongCommand(phase),
            phase with { Journal = phase.Journal with { EffectDigest = phase.Journal.ControlIntentDigest } }
        ];
        foreach (var changed in invalid)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Task.FromResult(
                RequestCqrsMovementJournalCapture.Capture(claim, original with
                {
                    NativeValue = changed
                }, voter, SiloAddress)));
        }
    }
}
