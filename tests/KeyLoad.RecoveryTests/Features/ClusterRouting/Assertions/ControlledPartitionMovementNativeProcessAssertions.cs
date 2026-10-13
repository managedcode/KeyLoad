using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeProcessAssertions
{
    private const int Version = 1;
    private const long FirstRevision = 1;
    private const long EmptyCut = 0;
    private const int PublishedParts = 1;
    private const int NoExaminedRecords = 0;
    private const string PutDocumentKind = "putDocument";
    private const string Principal = "root";
    private const string HealthyDocument = "knowledge-followup";
    private const string HealthyJson = "{\"text\":\"healthy\"}";
    private static readonly Guid HealthyId = Guid.Parse("2e9455b1-38d7-4c80-a56f-d5596e36b7ca");

    internal static async Task<(CommitReceipt Seed, DateTimeOffset RecordedAt)> SeedAsync(
        ControlledPartitionMovementNativeHost host, GrainOperationReply original, CancellationToken cancellationToken)
    {
        var entry = await ControlledPartitionMovementNativeProcessEntry.ReadAsync(host,
            ControlledPartitionMovementProcessLiteralCorpus.SeedId, cancellationToken).ConfigureAwait(false);
        var operation = entry.Operation ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        var expected = new CommitReceipt(ControlledPartitionMovementProcessLiteralCorpus.SeedId,
            new(host.Store.Identity.Incarnation, ControlledPartitionMovementProcessLiteralCorpus.Partition.AtomicPartitionId,
                entry.Index, FirstRevision), ControlledPartitionMovementProcessLiteralCorpus.ExpectedMutations(),
            DurabilityProfile.QuorumProcessDurable);
        await ReplyAsync(original, expected);
        await ControlledPartitionMovementProcessModelAssertions.ReadAsync(host, operation.EvaluatedAt, host.Store.Position, cancellationToken);
        return (expected, operation.EvaluatedAt);
    }

    internal static async Task<BlobCommitResult<BlobMetadata>> BlobAsync(ControlledPartitionMovementNativeHost host,
        CompleteBlobUploadRequest request, GrainOperationReply original, CancellationToken cancellationToken)
    {
        var entry = await ControlledPartitionMovementNativeProcessEntry.ReadAsync(host, request.CommandId, cancellationToken).ConfigureAwait(false);
        var operation = entry.Operation ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        var incarnation = host.Store.Identity.Incarnation;
        var metadata = new BlobMetadata(ControlledPartitionMovementProcessLiteralCorpus.Blob, FirstRevision,
            ControlledPartitionMovementProcessLiteralCorpus.BlobUploadId,
            ControlledPartitionMovementProcessLiteralCorpus.BlobBytes().Length, PublishedParts,
            ControlledPartitionMovementProcessLiteralCorpus.BlobIntegrityHash(incarnation), new RowAccess(), operation.EvaluatedAt, false);
        var expected = new BlobCommitResult<BlobMetadata>(new(request.CommandId,
            new(incarnation, ControlledPartitionMovementProcessLiteralCorpus.Partition.AtomicPartitionId, entry.Index, FirstRevision),
            [], DurabilityProfile.QuorumProcessDurable), metadata);
        await ReplyAsync(original, expected);
        await ControlledPartitionMovementProcessBlobAssertions.ReadAsync(host, metadata, cancellationToken);
        return expected;
    }

    internal static async Task<PartitionMovePhaseResult> PreparedAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMovementTransportRequest original, PartitionMovementAuthenticatedReply reply, CancellationToken cancellationToken)
    {
        var entry = await ControlledPartitionMovementNativeProcessEntry.ReadAsync(owners.Source, original.CommandId, cancellationToken).ConfigureAwait(false);
        var placement = original.Envelope.SourcePlacement;
        await Assert.That(placement.Partition).IsEqualTo(ControlledPartitionMovementProcessLiteralCorpus.Partition);
        var intent = new PartitionMoveIntent(Version, ControlledPartitionMovementProcessPrepareRequest.MoveId,
            placement.Partition, Principal, FirstRevision, placement, owners.Destination.Owner, entry.Index);
        var digest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(intent)));
        var control = new PartitionMoveControlRecord(Version, ControlledPartitionMovementProcessPrepareRequest.MoveId,
            placement.Partition, Principal, FirstRevision, placement, owners.Destination.Owner,
            PartitionMovePhase.Prepared, EmptyCut, entry.Index, null, null, null);
        var expected = new PartitionMovePhaseResult(ControlledPartitionMovementProcessPrepareRequest.MoveId,
            PartitionMovePeerStage.ControlPrepare, new(original.CommandId, owners.Control.Owner, entry.Index, digest),
            control, null, null, null);
        await RequirePreparedIdentityAsync(reply.Value.Reply, expected);
        await ReplyAsync(reply.Value.Reply, expected);
        return expected;
    }

    internal static async Task OutcomeAsync(ReadOnlyMemory<byte> originalBytes, PartitionMovePhaseResult expected)
    {
        var reply = NativeSerialization.Deserialize<PartitionMovementTransportReply>(originalBytes.Span);
        await Assert.That(reply.Reply.Error).IsNull();
        await Assert.That(reply.Reply.SafeDetail).IsNull();
        var witness = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementOutcomeWitness;
        await Assert.That(witness).IsNotNull();
        var actual = witness ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        await Assert.That(actual.ReadBytes).IsGreaterThan(EmptyCut);
        await Assert.That(actual.ExaminedRecords).IsGreaterThan(NoExaminedRecords);
        await Assert.That(actual.Result.Error).IsNull();
        await Assert.That(actual.Result.SafeDetail).IsNull();
        await ControlledPartitionMovementNativeProcessColdAssertions.EqualAsync(expected, actual.Result.Get<PartitionMovePhaseResult>());
    }

    internal static async Task<CommitReceipt> HealthyAsync(ControlledPartitionMovementNativeHost host,
        CommitReceipt? retained, CancellationToken cancellationToken)
    {
        var request = new CommandRequest(HealthyId, ControlledPartitionMovementProcessLiteralCorpus.Partition,
            [new PutDocument(ControlledPartitionMovementProcessLiteralCorpus.Collection, HealthyDocument, HealthyJson)]);
        var reply = await host.SubmitAsync(OperationKind.Batch, HealthyId, Principal, NativeSerialization.Serialize(request), cancellationToken);
        var entry = await ControlledPartitionMovementNativeProcessEntry.ReadAsync(host, HealthyId, cancellationToken).ConfigureAwait(false);
        var expected = retained ?? new CommitReceipt(HealthyId,
            new(host.Store.Identity.Incarnation, request.Partition.AtomicPartitionId, entry.Index, FirstRevision),
            [new(PutDocumentKind, ControlledPartitionMovementProcessLiteralCorpus.Collection, HealthyDocument, FirstRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await ReplyAsync(reply, expected);
        var reference = new EntityRef(request.Partition, ControlledPartitionMovementProcessLiteralCorpus.Collection, HealthyDocument);
        await ControlledPartitionMovementNativeProcessColdAssertions.EqualAsync(
            new DocumentResult(reference, FirstRevision, HealthyJson, false, []),
            host.Database.GetDocument(Principal, reference, cancellationToken: cancellationToken));
        return expected;
    }

    private static async Task RequirePreparedIdentityAsync(GrainOperationReply original, PartitionMovePhaseResult expected)
    {
        await Assert.That(original.Error).IsNull();
        await Assert.That(original.SafeDetail).IsNull();
        var actual = GrainNativePayload.Read<GrainValue>(original.Payload).Value as PartitionMovePhaseResult;
        await Assert.That(actual).IsNotNull();
        var phase = actual ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        await Assert.That(phase.MoveId).IsEqualTo(expected.MoveId);
        await Assert.That(phase.Stage).IsEqualTo(expected.Stage);
        await Assert.That(phase.Control).IsNotNull();
        var control = phase.Control ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        var expectedControl = expected.Control ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        await Assert.That(control.Version).IsEqualTo(expectedControl.Version);
        await Assert.That(control.MoveId).IsEqualTo(expectedControl.MoveId);
        await Assert.That(control.Partition).IsEqualTo(expectedControl.Partition);
        await Assert.That(ReferenceEquals(control.Partition, control.SourcePlacement.Partition)).IsTrue();
        await Assert.That(control.PrincipalId).IsEqualTo(expectedControl.PrincipalId);
        await Assert.That(control.PolicyEpoch).IsEqualTo(expectedControl.PolicyEpoch);
        await Assert.That(control.Phase).IsEqualTo(expectedControl.Phase);
        await Assert.That(control.SourceCut).IsEqualTo(expectedControl.SourceCut);
        await Assert.That(control.ControlPosition).IsEqualTo(expectedControl.ControlPosition);
        await Assert.That(control.ParentCheckpointRequired).IsEqualTo(expectedControl.ParentCheckpointRequired);
        await Assert.That(control.SourcePlacement.DirectoryRevision).IsEqualTo(expectedControl.SourcePlacement.DirectoryRevision);
        await Assert.That(control.SourcePlacement.Revision).IsEqualTo(expectedControl.SourcePlacement.Revision);
        await Assert.That(control.SourcePlacement.IsFallback).IsEqualTo(expectedControl.SourcePlacement.IsFallback);
        await ControlledPartitionMovementNativeProcessColdAssertions.EqualAsync(expectedControl.SourcePlacement, control.SourcePlacement);
        await ControlledPartitionMovementNativeProcessColdAssertions.EqualAsync(expectedControl.DestinationOwner, control.DestinationOwner);
        await Assert.That(control.ImageDigest).IsNull();
        await Assert.That(control.InstalledReceipt).IsNull();
        await Assert.That(control.PublishedPlacement).IsNull();
        await Assert.That(phase.Journal.CommandId).IsEqualTo(expected.Journal.CommandId);
        await Assert.That(phase.Journal.AppliedPosition).IsEqualTo(expected.Journal.AppliedPosition);
        await ControlledPartitionMovementNativeProcessColdAssertions.EqualAsync(expected.Journal.PhysicalOwner, phase.Journal.PhysicalOwner);
        await Assert.That(phase.Journal.EffectDigest).IsNull();
        await Assert.That(phase.Journal.ControlIntentDigest).IsEqualTo(expected.Journal.ControlIntentDigest);
        await Assert.That(phase.Fence).IsNull();
        await Assert.That(phase.InstalledReceipt).IsNull();
        await Assert.That(phase.PublishedPlacement).IsNull();
        await Assert.That(phase.Grant).IsNull();
        await Assert.That(phase.Cleanup).IsNull();
        await Assert.That(phase.ControlledCommand).IsNull();
        await Assert.That(phase.ControlledEffect).IsNull();
        await Assert.That(phase.ReceiverIssuance).IsNull();
        await Assert.That(phase.RetireCancellation).IsNull();
    }

    internal static async Task ReplyAsync<T>(GrainOperationReply original, T expected)
    {
        await Assert.That(original.Error).IsNull();
        await Assert.That(original.SafeDetail).IsNull();
        var actual = GrainNativePayload.Read<GrainValue>(original.Payload).Value;
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
