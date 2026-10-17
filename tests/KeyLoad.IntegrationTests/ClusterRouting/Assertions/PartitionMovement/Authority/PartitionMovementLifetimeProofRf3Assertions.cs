using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementLifetimeProofRf3Assertions
{
    private const long CreatedRevision = 1;
    private const long EmptyPosition = 0;
    private const string MutationKind = "putDocument";

    internal static async Task LiveAsync(PartitionMovementLifetimeProofRf3Original original,
        RequestCqrsControlledLifetimeStepWitness step, Dictionary<string, NativeCommandLifetimeCutWitness> actual,
        Dictionary<string, NativeCommandLifetimeCutWitness>? baseline)
    {
        await Assert.That(actual.Count).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var cut = actual[node];
            await Assert.That(cut.Request.CommandId).IsEqualTo(original.Command.CommandId);
            await Assert.That(cut.Request.EffectCommandId).IsEqualTo(step.EffectCommandId);
            await Assert.That(cut.Request.ArmId).IsEqualTo(original.ArmId);
            await Assert.That(cut.Request.Partition).IsEqualTo(original.Command.Partition);
            await Assert.That(cut.LogicalOutcome).IsNull();
            await Assert.That(cut.TechnicalOutcome).IsNull();
            if (baseline is null)
            {
                continue;
            }
            var before = baseline[node];
            await Assert.That(cut.ModelRowCount).IsEqualTo(before.ModelRowCount);
            await Assert.That(cut.ModelReadBytes).IsEqualTo(before.ModelReadBytes);
            await Assert.That(cut.ModelDigest).IsEqualTo(before.ModelDigest);
            await SameBytesAsync(before.LogicalOutcome, cut.LogicalOutcome);
            await SameBytesAsync(before.TechnicalOutcome, cut.TechnicalOutcome);
        }
        await Assert.That(original.Producer.IsCompleted).IsFalse();
    }

    internal static async Task PublicAsync(PartitionMovementPublicParentRf3Seed seed, PartitionMoveResult terminal,
        PartitionMovementLifetimeProofRf3Original original, CancellationToken token)
    {
        var receipt = original.Receipt ?? throw new InvalidDataException("The original receipt is absent.");
        await Assert.That(receipt.CommandId).IsEqualTo(original.Command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(original.Command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(terminal.DestinationOwner.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(terminal.PublishedPlacement!.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(EmptyPosition);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(new MutationReceipt[]
            { new(MutationKind, original.Entity.Collection, original.Entity.Id, CreatedRevision) }, receipt.Mutations.ToArray());
        await DocumentAsync(seed, original, token);
        await seed.VerifyAsync(token);
    }

    internal static async Task DocumentAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementLifetimeProofRf3Original original, CancellationToken token)
    {
        var expected = new DocumentResult(original.Entity, CreatedRevision, original.Json, false, []);
        await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(await seed.Source.GetAsync(original.Entity, token)));
        var official = await PublicReadAssertions.DocumentAsync(await seed.Official.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(original.Entity), token));
        await SqlRf3Protocol.EqualAsync(expected, official.Value);
    }

    internal static async Task DurableAsync(PartitionMovementPublicParentRf3Seed seed, PartitionMoveResult terminal,
        PartitionMovementLifetimeProofRf3Original original, PartitionMovementPublicParentRf3NativeCut[] cuts)
    {
        var controlCut = cuts.First(cut => cut.Header is not null);
        var identity = new PartitionControlCommandIdentity(CommandOutcomeScopeKind.Partition,
            seed.Partition, PartitionMovementPublicParentRf3Administrator.PrincipalId, original.Command.CommandId);
        var record = PartitionMovementCapturePointerRf3Fault.Read<PartitionControlCommandRecord>(controlCut,
            PartitionControlCommandKeys.Authority(identity));
        var effectId = record.EffectId;
        if (original.Baseline is { } captured)
        {
            await Assert.That(effectId).IsEqualTo(captured.EffectCommandId);
        }
        await Assert.That(record.Phase).IsEqualTo(PartitionControlCommandPhase.Finalized);
        var receivers = cuts.Where(cut => cut.Header is null).ToArray();
        await Assert.That(receivers.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var receiver in receivers)
        {
            var outcome = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(receiver,
                KeySpace.PartitionOutcome(seed.Partition, PartitionStoreProtocol.AdministratorId, effectId));
            var phase = outcome.Result.Get<PartitionMovePhaseResult>();
            await Assert.That(phase.Journal.CommandId).IsEqualTo(effectId);
            await SqlRf3Protocol.EqualAsync(terminal.DestinationOwner, phase.Journal.PhysicalOwner);
            var expected = new CommitReceipt(original.Command.CommandId,
                new(terminal.DestinationOwner.Incarnation, seed.Partition.AtomicPartitionId,
                    phase.Journal.AppliedPosition, terminal.PublishedPlacement!.PlacementEpoch),
                [new(MutationKind, original.Entity.Collection, original.Entity.Id, CreatedRevision)],
                DurabilityProfile.QuorumProcessDurable);
            await SqlRf3Protocol.EqualAsync(expected, original.Receipt!);
            await SqlRf3Protocol.EqualAsync(expected, phase.ControlledEffect!.OriginalResult.Get<CommitReceipt>());
            await SqlRf3Protocol.EqualAsync(expected with
            {
                CommandId = effectId
            }, phase.ControlledEffect.Receipt);
            foreach (var control in cuts.Where(cut => cut.Header is not null))
            {
                var logical = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(control,
                    KeySpace.PartitionOutcome(seed.Partition, PartitionMovementPublicParentRf3Administrator.PrincipalId, original.Command.CommandId));
                await SqlRf3Protocol.EqualAsync(expected, logical.Result.Get<CommitReceipt>());
            }
        }
    }

    private static async Task SameBytesAsync(ReadOnlyMemory<byte>? expected, ReadOnlyMemory<byte>? actual)
    {
        await Assert.That(actual.HasValue).IsEqualTo(expected.HasValue);
        if (expected.HasValue)
        {
            await Assert.That(actual!.Value.Span.SequenceEqual(expected.Value.Span)).IsTrue();
        }
    }
}
